using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает запрос на замену карты во время мулигана.
    /// Возвращает карту на дно колоды, берёт новую с вершины.
    /// </summary>
    public sealed class MulliganReplaceSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<MulliganReplaceRequest> _requestPool = default;
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsFilterInject<Inc<MulliganReplaceRequest>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var request = ref _requestPool.Value.Get(cardEntity);
                int playerEntity = request.PlayerEntity;

                _requestPool.Value.Del(cardEntity);

                if (!_mulliganPool.Value.Has(playerEntity))
                    continue;

                ref var mulligan = ref _mulliganPool.Value.Get(playerEntity);

                if (mulligan.Phase != MulliganPhase.Offering)
                    continue;

                if (mulligan.ReplacementsUsed >= mulligan.MaxReplacements)
                {
                    Debug.Log($"[MulliganReplaceSystem] No replacements left");
                    continue;
                }

                if (!mulligan.OfferedCards.Contains(cardEntity))
                    continue;

                ref var deck = ref _deckPool.Value.Get(playerEntity);
                ref var hand = ref _handPool.Value.Get(playerEntity);

                if (deck.Count == 0)
                    continue;

                // Убираем старую карту из руки, кладём на дно колоды
                RemoveFromHand(ref hand, cardEntity);
                PutToDeckBottom(ref deck, cardEntity);

                if (_handTagPool.Value.Has(cardEntity))
                    _handTagPool.Value.Del(cardEntity);
                if (!_deckTagPool.Value.Has(cardEntity))
                    _deckTagPool.Value.Add(cardEntity);

                // Берём новую с вершины
                int newCardEntity = deck.CardEntities[deck.Count - 1];
                deck.Count--;

                AddToHand(ref hand, newCardEntity);

                if (_deckTagPool.Value.Has(newCardEntity))
                    _deckTagPool.Value.Del(newCardEntity);
                if (!_handTagPool.Value.Has(newCardEntity))
                    _handTagPool.Value.Add(newCardEntity);

                // Заменяем в списке предложенных
                int idx = mulligan.OfferedCards.IndexOf(cardEntity);
                mulligan.OfferedCards[idx] = newCardEntity;
                mulligan.ReplacementsUsed++;

                GameEventBus.Publish(new MulliganCardReplacedEvent
                {
                    PlayerEntity = playerEntity,
                    OldCardEntity = cardEntity,
                    NewCardEntity = newCardEntity
                });
            }
        }

        private void RemoveFromHand(ref HandComponent hand, int cardEntity)
        {
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand.CardEntities[i] == cardEntity)
                {
                    for (int j = i; j < hand.Count - 1; j++)
                        hand.CardEntities[j] = hand.CardEntities[j + 1];
                    hand.Count--;
                    return;
                }
            }
        }

        private void AddToHand(ref HandComponent hand, int cardEntity)
        {
            if (hand.Count < HandComponent.MaxHandSize)
            {
                hand.CardEntities[hand.Count] = cardEntity;
                hand.Count++;
            }
        }

        private void PutToDeckBottom(ref DeckComponent deck, int cardEntity)
        {
            // Вставляем на дно (индекс 0), сдвигаем остальные
            int newSize = deck.Count + 1;
            if (newSize > deck.CardEntities.Length)
            {
                var newArr = new int[newSize];
                System.Array.Copy(deck.CardEntities, 1, newArr, 1, deck.Count);
                deck.CardEntities = newArr;
            }
            else
            {
                for (int i = deck.Count; i > 0; i--)
                    deck.CardEntities[i] = deck.CardEntities[i - 1];
            }
            deck.CardEntities[0] = cardEntity;
            deck.Count = newSize;
        }
    }
}
