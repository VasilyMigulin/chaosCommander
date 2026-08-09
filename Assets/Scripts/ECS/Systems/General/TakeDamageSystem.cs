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
    /// Если hp <= 0 → добавляет DeadTag + KilledByComponent (атрибуция убийцы). САМО начисление маны за
    /// килл — в DieSystem (единая точка для ВСЕХ способов убийства: урон здесь, Destroy-эффекты сами
    /// ставят KilledBy). Система гоняется на ОБОИХ клиентах (бой реплеится через
    /// RemoteCreatureAttackEvent→AttackSystem→AttackHitEvent), атрибуция идентична → синк даром.
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

        readonly EcsPoolInject<KilledByComponent> _killedByPool = default;   // атрибуция → мана в DieSystem
        readonly EcsPoolInject<ShieldComponent> _shieldPool = default;       // свойство «Защищённый»
        readonly EcsPoolInject<InvulnerableTag> _invulnerablePool = default; // свойство «Неуязвимый»
        readonly EcsPoolInject<VenomousComponent> _venomousPool = default;   // свойство «Ядовитый» (атакующий)
        readonly EcsPoolInject<PoisonComponent> _poisonPool = default;       // свойство «Отравленный» (цель)
        readonly EcsPoolInject<RetaliateTag> _retaliatePool = default;       // свойство «Ответочка»
        readonly EcsPoolInject<AttackComponent> _atkPool = default;

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

                // Односторонний бой: урон наносит ТОЛЬКО атакующий, цель НЕ отвечает (кроме «Ответочки» ниже).
                ApplyDamage(targetEntity, amount, attackerEntity, attackerOwner, targetOwner);

                // «Ответочка»: цель пережила бой и умеет отвечать → её Attack летит обратно атакующему той
                // же ApplyDamage (уважает Щит/Неуязвимость атакующего). Без повторной проверки Ответочки у
                // атакующего — иначе два таких существа дали бы бесконечный обмен ударами.
                if (_retaliatePool.Value.Has(targetEntity) && !_deadPool.Value.Has(targetEntity) && !_deadPool.Value.Has(attackerEntity))
                {
                    int counterAtk = _atkPool.Value.Has(targetEntity) ? _atkPool.Value.Get(targetEntity).Value : 0;
                    if (counterAtk > 0)
                        ApplyDamage(attackerEntity, counterAtk, targetEntity, targetOwner, attackerOwner);
                }
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

            // «Неуязвимый»: урон не проходит НИКОГДА (не тратит зарядов, в отличие от щита). НЕ блокирует
            // LethalHealthSystem (обнуление HP дебаффом статов) — тот минует TakeDamageEvent целиком.
            if (_invulnerablePool.Value.Has(entity)) return;

            // «Защищённый»: поглощает удар ЦЕЛИКОМ (0 урона), тратит 1 заряд; на 0 зарядов свойство снимается.
            // Единая точка для боя (AttackHitEvent) и урона от способностей (TakeDamageEvent) — оба сюда сходятся.
            if (_shieldPool.Value.Has(entity))
            {
                ref var shield = ref _shieldPool.Value.Get(entity);
                if (shield.Charges > 0)
                {
                    shield.Charges--;
                    if (shield.Charges <= 0) _shieldPool.Value.Del(entity);
                    return;
                }
            }

            // «Ядовитый» (АТАКУЮЩИЙ, sourceEntity): урон дошёл (не заблокирован Щитом/Неуязвимостью выше) →
            // цель получает стаки «Отравленного». Общая точка для боя И способностей — источник урона не
            // важен, важно только «этот урон нанёс носитель Ядовитого». Напрямую пишем PoisonComponent (не
            // через PoisonedProperty — Ecs.Systems не ссылается на Game.Core.Ability), как Щит/Неуязвимость
            // выше тоже манипулируют своими компонентами напрямую.
            if (sourceEntity >= 0 && _venomousPool.Value.Has(sourceEntity))
            {
                int venomStacks = _venomousPool.Value.Get(sourceEntity).Stacks;
                if (venomStacks > 0)
                {
                    if (!_poisonPool.Value.Has(entity)) _poisonPool.Value.Add(entity);
                    _poisonPool.Value.Get(entity).Stacks += venomStacks;
                }
            }

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

                    // Атрибуция убийцы → DieSystem начислит ману (враг владельца жертвы).
                    if (sourcePlayerId >= 0 && !_killedByPool.Value.Has(entity))
                        _killedByPool.Value.Add(entity).PlayerId = sourcePlayerId;
                }
            }
        }
    }
}
