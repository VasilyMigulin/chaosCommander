using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Instance.Card;
using Game.Core.Service;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunMulliganReplaceSystem : IEcsRunSystem
    { 
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<CardModelComponent> _cardModelPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsFilterInject<Inc<PlayerComponent, MulliganComponent, MulliganReplaceRequest>> _filter = default;
        readonly EcsPoolInject<MulliganReplaceRequest> _requestPool = default;
        readonly EcsPoolInject<MulliganCompleteEvent> _muliganConfirmPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var requestComp = ref _requestPool.Value.Get(entity);
                ref var mulliganComp = ref _mulliganPool.Value.Get(entity);
                 
                if (mulliganComp.Phase != MulliganPhase.Offering)
                    continue;

                ref var deckComp = ref _deckPool.Value.Get(entity);

                if (deckComp.Count == 0) continue;

                ref var handComp = ref _handPool.Value.Get(entity);

                int[] oldCardsEntities = requestComp.CardEntities;
                int[] nextCardsEntities = new int[requestComp.CardEntities.Length]; 

                int indexNextCard = 0;

                foreach (var cardEntity in requestComp.CardEntities)
                { 
                    if (!mulliganComp.OfferedCards.Contains(cardEntity))
                        continue;

                    RemoveFromHand(ref handComp, cardEntity);
                    PutToDeckBottom(ref deckComp, cardEntity);

                    _handTagPool.Value.Del(cardEntity);
                    _deckTagPool.Value.Add(cardEntity);

                    int nextCardEntity = -1;

                    for (int i = 0; i < deckComp.CardEntities.Count; i++)
                    {
                        if (_commanderPool.Value.Has(deckComp.CardEntities[i])) continue;
                        // Не брать ту же карту, которую только что вернули в колоду
                        if (deckComp.CardEntities[i] == cardEntity) continue;

                        nextCardEntity = deckComp.CardEntities[i];
                        break;
                    }

                    if (nextCardEntity == -1) continue;

                    deckComp.CardEntities.Remove(nextCardEntity);
                    deckComp.Count = deckComp.CardEntities.Count;

                    AddToHand(ref handComp, nextCardEntity);

                    _deckTagPool.Value.Del(nextCardEntity);
                    _handTagPool.Value.Add(nextCardEntity); 

                    int idx = mulliganComp.OfferedCards.IndexOf(cardEntity);
                    mulliganComp.OfferedCards[idx] = nextCardEntity; 
                     
                    nextCardsEntities[indexNextCard] = nextCardEntity;

                    indexNextCard++;
                }

                CardVisualData[] visualsData = new CardVisualData[requestComp.CardEntities.Length];

                for (int i = 0; i < visualsData.Length; i++)
                {
                    visualsData[i] = GetVisualForCard(nextCardsEntities[i]);
                }

                GameEventBus.Publish(new MulliganCardReplacedEvent
                {
                    OldCardEntity = oldCardsEntities,
                    NewCardEntity = nextCardsEntities,
                    NewCardVisual = visualsData
                });

                _requestPool.Value.Del(entity);

                _muliganConfirmPool.Value.Add(entity);
            } 
        }

        private void RemoveFromHand(ref HandComponent handComp, int cardEntity)
        {
            if (handComp.CardEntities.Contains(cardEntity))
            {
                handComp.CardEntities.Remove(cardEntity);
            }

            handComp.Count--; 
        }

        private void AddToHand(ref HandComponent hanComp, int cardEntity)
        {
            if (hanComp.Count < HandComponent.MaxHandSize)
            { 
                hanComp.CardEntities.Add(cardEntity);
                hanComp.Count++;
            }
        }

        private void PutToDeckBottom(ref DeckComponent deckComp, int cardEntity)
        {
            // Вставляем на дно (индекс 0), сдвигаем остальные

            deckComp.CardEntities.Add(cardEntity);
            deckComp.CardEntities.Shuffle();
            deckComp.Count = deckComp.CardEntities.Count; 
        }

        private CardVisualData GetVisualForCard(int cardEntity)
        {
            if (!_cardModelPool.Value.Has(cardEntity)) return default;
            ref var model = ref _cardModelPool.Value.Get(cardEntity);
            var instance = _cardConfig.Value.Get(model.ExpansionId, model.ModelId);
            return instance?.CardData != null ? CardVisualDataFactory.From(instance.CardData) : default;
        }

    }
}