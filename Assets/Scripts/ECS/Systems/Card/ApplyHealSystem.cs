using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает HealEffectComponent на effect entity.
    /// Применяет лечение к HealthComponent на TargetEntity.
    /// </summary>
    public sealed class ApplyHealSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, HealEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<HealEffectComponent> _healPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var heal = ref _healPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_hpPool.Value.Has(targetEntity))
                {
                    ref var hp = ref _hpPool.Value.Get(targetEntity);
                    hp.Current = System.Math.Min(hp.Current + heal.Amount, hp.Max);
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
