using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает DrawCardEvent на сущности игрока:
    ///   - берёт верхнюю карту из DeckComponent
    ///   - если рука не переполнена — перевешивает DeckTag → HandTag
    ///   - если переполнена (> MaxHandSize) — вешает BurnEvent
    /// </summary>
    public sealed class DrawCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<DrawCardEvent> _drawPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<BurnEvent> _burnPool = default;
        readonly EcsFilterInject<Inc<DrawCardEvent, DeckComponent, HandComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var playerEntity in _filter.Value)
            {
                ref var deck = ref _deckPool.Value.Get(playerEntity);
                ref var hand = ref _handPool.Value.Get(playerEntity);

                if (deck.Count == 0)
                {
                    _drawPool.Value.Del(playerEntity);
                    continue;
                }

                int cardEntity = deck.CardEntities[0];

                // сдвигаем колоду
                for (int i = 0; i < deck.Count - 1; i++)
                    deck.CardEntities[i] = deck.CardEntities[i + 1];
                deck.Count--;

                if (hand.Count < HandComponent.MaxHandSize)
                {
                    hand.CardEntities[hand.Count] = cardEntity;
                    hand.Count++;

                    if (_deckTagPool.Value.Has(cardEntity))
                        _deckTagPool.Value.Del(cardEntity);

                    _handTagPool.Value.Add(cardEntity);

                    GameEventBus.Publish(new CardDrawnEvent
                    {
                        CardEntity = cardEntity,
                        PlayerId = playerEntity
                    });
                }
                else
                {
                    // рука переполнена — карта сгорает
                    if (!_burnPool.Value.Has(cardEntity))
                        _burnPool.Value.Add(cardEntity);
                }

                _drawPool.Value.Del(playerEntity);
            }
        }
    }
}
