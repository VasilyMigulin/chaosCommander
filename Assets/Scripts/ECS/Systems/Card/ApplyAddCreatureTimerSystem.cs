using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    public sealed class ApplyAddCreatureTimerSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, AddCreatureTimerEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<AddCreatureTimerEffectComponent> _addPool = default;
        readonly EcsPoolInject<CreatureTimerComponent> _timerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int turns = _addPool.Value.Get(effectEntity).Turns;
                if (target >= 0 && turns > 0)
                {
                    if (!_timerPool.Value.Has(target))
                        _timerPool.Value.Add(target);
                    _timerPool.Value.Get(target).TurnsRemaining = turns;
                }
                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
