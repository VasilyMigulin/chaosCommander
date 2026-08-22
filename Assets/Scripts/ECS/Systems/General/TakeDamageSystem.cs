using System;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
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
        readonly EcsPoolInject<HandTag> _handPool = default;   // фидбэк «урон карте в руке» (см. ApplyDamage)
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<LastDamageTakenComponent> _lastDmgPool = default;

        readonly EcsPoolInject<KilledByComponent> _killedByPool = default;   // атрибуция → мана в DieSystem
        readonly EcsPoolInject<ShieldComponent> _shieldPool = default;       // свойство «Укреплённый»
        readonly EcsPoolInject<InvulnerableTag> _invulnerablePool = default; // свойство «Неуязвимый»
        readonly EcsPoolInject<VenomousComponent> _venomousPool = default;   // свойство «Ядовитый» (атакующий)
        readonly EcsPoolInject<PoisonComponent> _poisonPool = default;       // свойство «Отравленный» (цель)
        readonly EcsPoolInject<RetaliateTag> _retaliatePool = default;       // свойство «Ответочка»
        readonly EcsPoolInject<VampirismTag> _vampirismPool = default;       // свойство «Вампиризм»
        readonly EcsPoolInject<AttackComponent> _atkPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;   // для PlayerEntityById (Вампиризм)

        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsCustomInject<DefaultAbilityVfxConfig> _defaultVfx = default;   // ShieldedBlockVfxPrefab

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

        // Сущность игрока-аватара по PlayerId (обратная операция к PlayerIdOf) — нужна Вампиризму, чтобы
        // лечить именно ВЛАДЕЛЬЦА, а не носителя свойства. Игроков всегда 2 — линейный проход дёшев.
        int PlayerEntityById(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }

        void ApplyDamage(int entity, int amount, int sourceEntity, int sourcePlayerId, int targetPlayerId)
        { 
            if (!_hpPool.Value.Has(entity)) return;
            if (_deadPool.Value.Has(entity)) return;

            // «Неуязвимый»: урон не проходит НИКОГДА (не тратит зарядов, в отличие от щита). НЕ блокирует
            // LethalHealthSystem (обнуление HP дебаффом статов) — тот минует TakeDamageEvent целиком.
            if (_invulnerablePool.Value.Has(entity)) return;

            // «Укреплённый» (Shielded): поглощает удар ЦЕЛИКОМ (0 урона), тратит 1 заряд; на 0 зарядов
            // свойство снимается. Единая точка для боя (AttackHitEvent) и урона от способностей
            // (TakeDamageEvent) — оба сюда сходятся. Дефолтный VFX (ShieldedBlockVfxPrefab) — играет на
            // КАЖДОМ поглощённом ударе; у свойства нет своей Vfx-спеки (в отличие от способностей),
            // это единственный источник картинки блока.
            if (_shieldPool.Value.Has(entity))
            {
                ref var shield = ref _shieldPool.Value.Get(entity);
                if (shield.Charges > 0)
                {
                    shield.Charges--;
                    if (shield.Charges <= 0)
                    {
                        _shieldPool.Value.Del(entity);
                        // Реактивно гасим постоянный визуал (PropertyAuraVisualSystem) — заряды спалены в ноль,
                        // а не через ShieldedProperty.Remove (Ecs.Systems не ссылается на Game.Core.Ability).
                        GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = "Shielded", Active = false });
                    }

                    var prefab = _defaultVfx.Value != null ? _defaultVfx.Value.ShieldedBlockVfxPrefab : null;
                    if (prefab != null && EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, entity, out var at))
                        GameEventBus.Publish(new HitVfxEvent { At = at, Prefab = prefab });

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

                    // Значок «Отравлен» над головой ЦЕЛИ (постоянный, пока Stacks > 0 — снимает только
                    // DieSystem) + разовая вспышка попадания ядом. Active=true идемпотентен — можно слать
                    // при КАЖДОМ наложении, PropertyAuraVisualSystem не пересоздаст, если уже показан.
                    GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = "Poisoned", Active = true });

                    var poisonHitPrefab = _defaultVfx.Value != null ? _defaultVfx.Value.PoisonedHitVfxPrefab : null;
                    if (poisonHitPrefab != null && EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, entity, out var poisonAt))
                        GameEventBus.Publish(new HitVfxEvent { At = poisonAt, Prefab = poisonHitPrefab });
                }
            }

            ref var hp = ref _hpPool.Value.Get(entity);
            hp.Current -= amount;

            // Фидбэк «урон карте В РУКЕ» (напр. существо задето Zone=Hand/Any способностью, пока не разыграно).
            // CardFeedbackUtil (Game.Core.Ability) отсюда не позвать — Ecs.Systems на Ability не ссылается
            // (см. Poison/Shield выше в этом же файле) — дублируем его логику: HandTag → событие для UI.
            if (_handPool.Value.Has(entity))
                GameEventBus.Publish(new CardAffectedInHandUIEvent { CardEntity = entity, Kind = CardAffectKind.Damaged });

            // «Вампиризм» (АТАКУЮЩИЙ, sourceEntity): урон РЕАЛЬНО прошёл (не заблокирован Щитом/Неуязвимостью
            // выше) → лечит ВЛАДЕЛЬЦА носителя (игрока-аватар), а не сам носитель, на ту же величину.
            // Владельца-сущность ищем по PlayerId (2 игрока — линейный проход по фильтру дёшев).
            if (amount > 0 && sourceEntity >= 0 && _vampirismPool.Value.Has(sourceEntity) && sourcePlayerId >= 0)
            {
                int ownerPlayerEntity = PlayerEntityById(sourcePlayerId);
                if (ownerPlayerEntity >= 0 && _hpPool.Value.Has(ownerPlayerEntity))
                {
                    ref var ownerHp = ref _hpPool.Value.Get(ownerPlayerEntity);
                    ownerHp.Current = Math.Min(ownerHp.Current + amount, ownerHp.Max);

                    // Разовые вспышки: «укус» на ЦЕЛИ урона + «пришедшее» здоровье на АВАТАРЕ владельца.
                    var hitPrefab = _defaultVfx.Value != null ? _defaultVfx.Value.VampirismHitVfxPrefab : null;
                    if (hitPrefab != null && EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, entity, out var hitAt))
                        GameEventBus.Publish(new HitVfxEvent { At = hitAt, Prefab = hitPrefab });

                    var healPrefab = _defaultVfx.Value != null ? _defaultVfx.Value.VampirismHealVfxPrefab : null;
                    if (healPrefab != null && EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, ownerPlayerEntity, out var healAt))
                        GameEventBus.Publish(new HitVfxEvent { At = healAt, Prefab = healPrefab });
                }
            }

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
