using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет урон из AttackHitEvent и TakeDamageEvent:
    ///   - AttackHitEvent (на атакующем): урон цели, после — взаимный урон цели атакующему.
    ///   - TakeDamageEvent (на цели): прямой урон из способностей.
    /// Если hp <= 0 → добавляет DeadTag.
    /// БАЗОВЫЙ ДОХОД МАНЫ: убийство ВРАЖЕСКОГО существа даёт ману убийце (мана стартует 0/0 и доходом хода
    /// не растёт — единственный пассивный источник). Начисляем здесь: система гоняется на ОБОИХ клиентах
    /// (бой реплеится через RemoteCreatureAttackEvent→AttackSystem→AttackHitEvent), атрибуция убийцы
    /// (sourcePlayerId) идентична → мана сходится без отдельного синка.
    /// </summary>
    public sealed class TakeDamageSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        // AttackHitEvent живёт на атакующем
        readonly EcsFilterInject<Inc<AttackHitEvent>, Exc<DeadTag>> _hitFilter = default;
        readonly EcsPoolInject<AttackHitEvent> _hitPool = default;

        // TakeDamageEvent живёт на цели (из способностей / эффектов)
        readonly EcsFilterInject<Inc<TakeDamageEvent, HealthComponent>> _dmgFilter = default;
        readonly EcsPoolInject<TakeDamageEvent> _dmgPool = default;

        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<LastDamageTakenComponent> _lastDmgPool = default;

        // Мана-доход за килл
        readonly EcsPoolInject<CreatureTag> _creaturePool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, ManaComponent>> _playerManaFilter = default;

        const int ManaPerKill = 1;   // сколько маны за убитое вражеское существо (баланс-кнопка)
        const int ManaCap     = 10;  // потолок маны (как у золота)

        public void Run(IEcsSystems systems)
        {
            // ── AttackHitEvent ──────────────────────────────────────────────
            foreach (var attackerEntity in _hitFilter.Value)
            {
                ref var hit = ref _hitPool.Value.Get(attackerEntity);
                int targetEntity = hit.TargetEntity;
                int amount       = hit.Amount;
                _hitPool.Value.Del(attackerEntity);
                 
                if (_deadPool.Value.Has(targetEntity)) continue;

                int attackerOwner = PlayerIdOf(attackerEntity);
                int targetOwner   = PlayerIdOf(targetEntity);

                // Односторонний бой: урон наносит ТОЛЬКО атакующий, цель НЕ отвечает.
                ApplyDamage(targetEntity, amount, attackerEntity, attackerOwner, targetOwner);
            }

            // ── TakeDamageEvent (от способностей) ──────────────────────────
            foreach (var entity in _dmgFilter.Value)
            {
                ref var dmg = ref _dmgPool.Value.Get(entity);
                int srcOwner = PlayerIdOf(dmg.Attacker);
                int tgtOwner = PlayerIdOf(entity);
                ApplyDamage(entity, dmg.Amount, dmg.Attacker, srcOwner, tgtOwner);
                _dmgPool.Value.Del(entity);
            }
        }

        // PlayerId сущности как участника урона. У АВАТАРА игрока OwnerComponent НЕТ (только PlayerComponent),
        // поэтому его надо брать из PlayerComponent — иначе урон игроку получал TargetPlayerId=-1, и трекер
        // «нанёс себе на своём ходу» (Вуду-будду: Source==Target==текущий ход) не срабатывал НИКОГДА
        // (напр. самоурон от Ритуального костра в начале хода не копился в PlayerDamageTakenOwnTurn).
        // У карты/существа участник — владелец (OwnerComponent). Порядок проверок: сперва игрок, потом owner.
        int PlayerIdOf(int entity)
        {
            if (entity < 0) return -1;
            if (_playerPool.Value.Has(entity)) return _playerPool.Value.Get(entity).PlayerId;
            if (_ownerPool.Value.Has(entity))  return _ownerPool.Value.Get(entity).OwnerId;
            return -1;
        }

        void ApplyDamage(int entity, int amount, int sourceEntity, int sourcePlayerId, int targetPlayerId)
        { 
            if (!_hpPool.Value.Has(entity)) return;
            if (_deadPool.Value.Has(entity)) return;

            ref var hp = ref _hpPool.Value.Get(entity);
            hp.Current -= amount;

            GameEventBus.Publish(new CreatureDamagedEvent { CreatureEntity = entity, Amount = amount });

            // Урон ИГРОКУ (а не существу) — для условий (OwnerHealthBelow) и редиректа (Вуду-будду).
            if (_playerPool.Value.Has(entity))
            {
                if (!_lastDmgPool.Value.Has(entity)) _lastDmgPool.Value.Add(entity);
                _lastDmgPool.Value.Get(entity).Amount = amount;   // величина для эффекта-редиректа
                GameEventBus.Publish(new PlayerDamagedEvent { PlayerEntity = entity, Amount = amount });
            }

            GameEventBus.Publish(new DamageTrackedEvent
            {
                SourceEntity   = sourceEntity,
                TargetEntity   = entity,
                SourcePlayerId = sourcePlayerId,
                TargetPlayerId = targetPlayerId,
                Amount         = amount,
            });

            if (hp.Current <= 0)
            {
                hp.Current = 0;
                // Игрок НЕ получает DeadTag: это маркер существ (DieSystem игрока не обрабатывает),
                // а его наличие мешало бы лечению в каскаде отменить поражение. Конец матча по HP≤0
                // определяет GameOverCheckSystem после оседания каскада.
                if (!_playerPool.Value.Has(entity))
                {
                    _deadPool.Value.Add(entity);

                    // Доход маны: убил ВРАЖЕСКОЕ существо (не своё) → +мана убийце.
                    if (_creaturePool.Value.Has(entity) && sourcePlayerId >= 0 && sourcePlayerId != targetPlayerId)
                        AwardManaForKill(sourcePlayerId);
                }
            }
        }

        // Начисляет ману игроку-убийце (поднимает Max и Current, кап ManaCap — как у золота). Публикует
        // ResourceChangedEvent для UI. Гоняется на обоих клиентах → значения сходятся без отдельного синка.
        void AwardManaForKill(int playerId)
        {
            foreach (var pe in _playerManaFilter.Value)
            {
                ref var p = ref _playerPool.Value.Get(pe);
                if (p.PlayerId != playerId) continue;

                ref var mana = ref _manaPool.Value.Get(pe);
                mana.Max     = System.Math.Min(mana.Max + ManaPerKill, ManaCap);
                mana.Current = System.Math.Min(mana.Current + ManaPerKill, mana.Max);

                GameEventBus.Publish(new ResourceChangedEvent
                {
                    isLocalPlayer = p.IsLocalPlayer,
                    Type          = EnumService.ResourceType.Mana,
                    NewValue      = mana.Current,
                    MaxValue      = mana.Max,
                });
                return;
            }
        }
    }
}
