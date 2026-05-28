using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает DrawEffectComponent на effect entity (HitComponent + TargetEntityComponent).
    /// Добавляет DrawCardEvent на targetEntity, затем удаляет effect entity.
    /// </summary>
    public sealed class ApplyDrawSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, DrawEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<DrawEffectComponent> _drawEffectPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DrawCardEvent> _drawCardPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _drawEffectPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_drawCardPool.Value.Has(targetEntity))
                    _drawCardPool.Value.Get(targetEntity).Count += effect.Count;
                else
                {
                    ref var drawEvent = ref _drawCardPool.Value.Add(targetEntity);
                    drawEvent.Count = effect.Count;
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
