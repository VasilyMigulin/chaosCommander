using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunFirstTurnStartSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<PlayerComponent, DeckComponent, HandComponent, MatchStartEvent>> _filter = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<MatchStartEvent> _matchStartEvent = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var deckComp = ref _deckPool.Value.Get(entity);
                ref var handComp = ref _handPool.Value.Get(entity);

                foreach (var cardEntity in deckComp.CardEntities)
                {
                    _matchStartEvent.Value.Add(cardEntity);
                }

                foreach (var cardEntity in handComp.CardEntities)
                { 
                    _matchStartEvent.Value.Add(cardEntity);
                }
            }
        }
    }
}