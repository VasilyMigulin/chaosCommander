using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunTurnStartCardSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<TurnStartEvent, AbilityContainerComponent, BoardTag>, Exc<HandTag, DeckTag>> _filter = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<TurnStartEvent> _castPool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var abilityContainerComp = ref _abilityContainerPool.Value.Get(entity);

                foreach (var abilityEntity in abilityContainerComp.AbilityEntities)
                {
                    _castPool.Value.Add(abilityEntity);
                }
            }
        }
    }
}