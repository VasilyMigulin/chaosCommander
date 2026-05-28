using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    public sealed class RunAbilityDieSystem : IEcsRunSystem
    {
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsFilterInject<Inc<DieEvent, OnDieTrigger>> _filter = default; 
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _filter.Value)
            {
                if (_state.Value.TryGetEntity(EntityService.ABILITY_QUEUE_ENTITY, out int queueEntity))
                {
                    _queuePool.Value.Get(queueEntity).Push(abilityEntity);
                }
            }
        }
    }
}
