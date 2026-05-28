using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает DrawCardEvent на сущности игрока:
    ///   - берёт верхнюю карту из DeckComponent
    ///   - если в руке меньше MaxNonCommanderCards обычных карт — перевешивает DeckTag → HandTag
    ///   - если рука полна — карта ОСТАЁТСЯ в колоде (не добираем, не сжигаем)
    ///
    /// Сжигание карт — отдельная механика способностей (см. BurnCardSystem), здесь не применяется.
    /// </summary>
    public sealed class DrawCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<DrawCardEvent> _drawPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsFilterInject<Inc<DrawCardEvent, DeckComponent, HandComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var playerEntity in _filter.Value)
            {
                ref var drawEvent = ref _drawPool.Value.Get(playerEntity);
                int count = drawEvent.Count > 0 ? drawEvent.Count : 1;

                for (int c = 0; c < count; c++)
                {
                    ref var deck = ref _deckPool.Value.Get(playerEntity);
                    ref var hand = ref _handPool.Value.Get(playerEntity);

                    if (deck.Count == 0) break;

                    // Рука полна — карта остаётся в колоде (без сжигания)
                    if (CountNonCommander(ref hand) >= HandComponent.MaxNonCommanderCards)
                        break;

                    int cardEntity = deck.CardEntities[0];

                    deck.CardEntities.RemoveAt(0);
                    deck.Count--;

                    hand.CardEntities.Add(cardEntity);
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

                _drawPool.Value.Del(playerEntity);
            }
        }

        private int CountNonCommander(ref HandComponent hand)
        {
            int n = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                if (!_commanderPool.Value.Has(hand.CardEntities[i]))
                    n++;
            }
            return n;
        }
    }
}
