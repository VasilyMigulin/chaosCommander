using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    public sealed class RunAbilityCastSystem : IEcsRunSystem
    {
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsFilterInject<Inc<CastEvent, OnCastTrigger>> _filter = default;
        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsPoolInject<AbilityChosenTargetComponent> _chosenTargetPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _filter.Value)
            {
                ref var castEvent = ref _castPool.Value.Get(abilityEntity);

                // Сохраняем явно выбранную цель на entity способности
                if (castEvent.TargetEntity != -1 && !_chosenTargetPool.Value.Has(abilityEntity))
                {
                    ref var chosen = ref _chosenTargetPool.Value.Add(abilityEntity);
                    chosen.TargetEntity = castEvent.TargetEntity;
                }

                if (_state.Value.TryGetEntity(EntityService.ABILITY_QUEUE_ENTITY, out int queueEntity))
                {
                    _queuePool.Value.Get(queueEntity).Push(abilityEntity);
                }
            }
        }
    }
}
