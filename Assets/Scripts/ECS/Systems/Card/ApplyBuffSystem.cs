using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Buff: ПЕРМАНЕНТНО изменяет статы цели.
    /// Пишет в Base/BaseMax (чтобы AuraRecalcSystem не затирал бафф съёмными аурами)
    /// и в эффективные Value/Max/Current (для немедленного эффекта и для целей-игроков,
    /// которых AuraRecalcSystem не пересчитывает).
    /// </summary>
    public sealed class ApplyBuffSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, BuffEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<BuffEffectComponent> _buffPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var buff = ref _buffPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (buff.AttackBonus != 0 && _attackPool.Value.Has(targetEntity))
                {
                    ref var atk = ref _attackPool.Value.Get(targetEntity);
                    atk.Base  += buff.AttackBonus;
                    atk.Value += buff.AttackBonus;
                }

                if (buff.HealthBonus != 0 && _hpPool.Value.Has(targetEntity))
                {
                    ref var hp = ref _hpPool.Value.Get(targetEntity);
                    hp.BaseMax += buff.HealthBonus;
                    hp.Max     += buff.HealthBonus;
                    hp.Current += buff.HealthBonus;
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
