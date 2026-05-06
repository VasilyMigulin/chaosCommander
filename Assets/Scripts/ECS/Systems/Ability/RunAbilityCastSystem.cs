using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    public sealed class RunAbilityCastSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<CastEvent, AbilityContainerComponent>> _filter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>> _queueFilter = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsPoolInject<OnCastTrigger> _triggerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var container = ref _abilityContainerPool.Value.Get(cardEntity);
                if (container.AbilityEntities == null) continue;

                foreach (var queueEntity in _queueFilter.Value)
                {
                    ref var queue = ref _queuePool.Value.Get(queueEntity);
                    foreach (var abilityEntity in container.AbilityEntities)
                    {
                        if (_triggerPool.Value.Has(abilityEntity))
                            queue.Push(abilityEntity);
                    }
                    break;
                }
            }
        }
    }
}
