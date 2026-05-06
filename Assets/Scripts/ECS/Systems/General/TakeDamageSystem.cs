using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет урон из AttackHitEvent и TakeDamageEvent:
    ///   - AttackHitEvent (на атакующем): урон цели, после — взаимный урон цели атакующему.
    ///   - TakeDamageEvent (на цели): прямой урон из способностей.
    /// Если hp <= 0 → добавляет DeadTag.
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
        readonly EcsPoolInject<AttackComponent> _atkPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

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

                int attackerOwner = _ownerPool.Value.Has(attackerEntity) ? _ownerPool.Value.Get(attackerEntity).OwnerId : -1;
                int targetOwner   = _ownerPool.Value.Has(targetEntity)   ? _ownerPool.Value.Get(targetEntity).OwnerId   : -1;

                // Урон цели
                ApplyDamage(targetEntity, amount, attackerEntity, attackerOwner, targetOwner);

                // Ответный урон атакующему от цели (если жива)
                if (!_deadPool.Value.Has(targetEntity) && _atkPool.Value.Has(targetEntity))
                {
                    int counterDmg = _atkPool.Value.Get(targetEntity).Value;
                    ApplyDamage(attackerEntity, counterDmg, targetEntity, targetOwner, attackerOwner);
                }
            }

            // ── TakeDamageEvent (от способностей) ──────────────────────────
            foreach (var entity in _dmgFilter.Value)
            {
                ref var dmg = ref _dmgPool.Value.Get(entity);
                int srcOwner = _ownerPool.Value.Has(dmg.Attacker) ? _ownerPool.Value.Get(dmg.Attacker).OwnerId : -1;
                int tgtOwner = _ownerPool.Value.Has(entity)       ? _ownerPool.Value.Get(entity).OwnerId       : -1;
                ApplyDamage(entity, dmg.Amount, dmg.Attacker, srcOwner, tgtOwner);
                _dmgPool.Value.Del(entity);
            }
        }

        void ApplyDamage(int entity, int amount, int sourceEntity, int sourcePlayerId, int targetPlayerId)
        { 
            if (!_hpPool.Value.Has(entity)) return;
            if (_deadPool.Value.Has(entity)) return;

            ref var hp = ref _hpPool.Value.Get(entity);
            hp.Current -= amount;

            GameEventBus.Publish(new CreatureDamagedEvent { CreatureEntity = entity, Amount = amount });

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
                _deadPool.Value.Add(entity);
            }
        }
    }
}
