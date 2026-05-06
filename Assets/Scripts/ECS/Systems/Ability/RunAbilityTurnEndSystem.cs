using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    public sealed class RunAbilityTurnEndSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<TurnEndEvent, AbilityContainerComponent, OnTurnEndTrigger>> _filter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>> _queueFilter = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var container = ref _abilityContainerPool.Value.Get(entity);

                if (container.AbilityEntities == null) continue;

                foreach (var queueEntity in _queueFilter.Value)
                {
                    ref var queue = ref _queuePool.Value.Get(queueEntity);
                    foreach (var abilityEntity in container.AbilityEntities)
                    {
                        queue.Push(abilityEntity);
                    }
                    break;
                }
            }
        }
    }
}
