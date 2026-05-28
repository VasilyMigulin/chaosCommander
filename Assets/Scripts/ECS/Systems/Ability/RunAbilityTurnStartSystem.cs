using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    public sealed class RunAbilityTurnStartSystem : IEcsRunSystem
    {
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsFilterInject<Inc<TurnStartEvent, OnTurnStartTrigger>> _filter = default; 
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
