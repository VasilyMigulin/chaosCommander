using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunCastCardSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<CastEvent, AbilityContainerComponent, HandTag>> _filter = default; 
        readonly EcsPoolInject<DeckTag> _deckPool  = default; 
        readonly EcsPoolInject<HandTag> _handPool  = default; 
        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        
        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var abilityContainerComp = ref _abilityContainerPool.Value.Get(entity);

                foreach (var abilityEntity in abilityContainerComp.AbilityEntities)
                {
                    _castPool.Value.Add(abilityEntity);
                }

                _handPool.Value.Del(entity);
                _deckPool.Value.Del(entity);
            }
        }
    }
}