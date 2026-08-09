using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Instance.Card;
using Game.Core.Shared;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{  
    public sealed class InitMulliganSystem : IEcsInitSystem
    { 
        readonly EcsFilterInject<Inc<DeckComponent, HandComponent, PlayerComponent, PlayerSideComponent, LocalComponent>> _filter = default;

        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<CardModelComponent> _cardModelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewDataPool = default;   // запечённый визуал (имя/статы/арт) — как у боевых вьюх
        readonly EcsPoolInject<CommanderTag> _commanderTagPool = default;
        readonly EcsPoolInject<MulliganModifierComponent> _mulliganModPool = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        public void Init(IEcsSystems systems)
        {  
            foreach (var playerEntity in _filter.Value)
            {
                ref var side = ref _sidePool.Value.Get(playerEntity);
                int offerCount = side.Side == 1 ? 3 : 4;
                int maxReplacements = side.Side == 1 ? 1 : 2;

                ref var deck = ref _deckPool.Value.Get(playerEntity);
                ref var hand = ref _handPool.Value.Get(playerEntity);
                ref var player = ref _playerPool.Value.Get(playerEntity);

                // Модификатор мулигана (Били): сканируем колоду на маркер ДО раздачи.
                // «начинаете с N карт» (абсолют) + «замена любого числа».
                bool unlimitedReplace = false;
                for (int i = 0; i < deck.CardEntities.Count; i++)
                {
                    if (!_mulliganModPool.Value.Has(deck.CardEntities[i])) continue;
                    var muliganModifierComp = _mulliganModPool.Value.Get(deck.CardEntities[i]);
                    if (muliganModifierComp.StartingHand > offerCount) offerCount = muliganModifierComp.StartingHand;
                    if (muliganModifierComp.UnlimitedReplace) unlimitedReplace = true;
                }

                // Считаем доступные карты (командир не участвует в маллигане)
                int availableCount = 0;
                for (int i = 0; i < deck.CardEntities.Count; i++)
                    if (!_commanderTagPool.Value.Has(deck.CardEntities[i]))
                        availableCount++;

                if (availableCount < offerCount)
                {
                    Debug.LogWarning($"[MulliganOfferSystem] Player {player.PlayerId}: not enough non-commander cards in deck ({availableCount} < {offerCount})");
                    offerCount = availableCount;
                }

                var offeredCards = new List<int>(offerCount);
                int taken = 0;
                int searchIndex = deck.CardEntities.Count - 1;
                while (taken < offerCount && searchIndex >= 0)
                {
                    int cardEntity = deck.CardEntities[searchIndex];

                    // Карта командира не участвует в маллигане — пропускаем
                    if (_commanderTagPool.Value.Has(cardEntity))
                    {
                        searchIndex--;
                        continue;
                    }

                    offeredCards.Add(cardEntity);

                    if (_deckTagPool.Value.Has(cardEntity))
                        _deckTagPool.Value.Del(cardEntity);

                    if (!_handTagPool.Value.Has(cardEntity))
                        _handTagPool.Value.Add(cardEntity);

                    hand.CardEntities.Add(cardEntity);

                    deck.CardEntities.RemoveAt(searchIndex);
                    deck.Count--;
                    searchIndex--;

                    taken++;
                }

                hand.Count += taken;
                offerCount = taken;

                if (unlimitedReplace) maxReplacements = offerCount;   // «любое число» = можно заменить все предложенные

                ref var mulligan = ref _mulliganPool.Value.Add(playerEntity);
                mulligan.Phase = MulliganPhase.Offering;
                mulligan.OfferedCards = offeredCards;
                mulligan.MaxReplacements = maxReplacements; 

                Debug.Log($"[InitMulliganSystem] Player {player.PlayerId} side={side.Side}: publishing MulliganStartedEvent with {offerCount} cards, maxReplacements={maxReplacements}.");

                GameEventBus.Publish(new MulliganStartedEvent
                {
                    PlayerEntity = playerEntity,
                    OfferedCardEntities = offeredCards.ToArray(),
                    OfferedCardVisuals = BuildVisuals(offeredCards),
                    MaxReplacements = maxReplacements
                });

                Debug.Log($"[InitMulliganSystem] Player {player.PlayerId} side={side.Side}: MulliganStartedEvent published. offered={offerCount}, maxReplacements={maxReplacements}");
            }
        }

        CardVisualData[] BuildVisuals(List<int> cardEntities)
        {
            var result = new CardVisualData[cardEntities.Count];
            for (int i = 0; i < cardEntities.Count; i++)
                result[i] = GetVisualForCard(cardEntities[i]);
            return result;
        }

        CardVisualData GetVisualForCard(int cardEntity)
        {
            // Берём ЗАПЕЧЁННЫЙ визуал сущности (CardModel.Init его пишет — имя/статы/стоимость/арт, уже с
            // локализацией и форматтером), как боевые вьюхи. НЕ ре-резолвим через config.Get: стори-карты
            // вне экспаншенов (ExpansionId пустой) он не находит → раньше мулиган показывал пустые карты.
            if (_viewDataPool.Value.Has(cardEntity))
                return CardVisualDataFactory.From(_viewDataPool.Value.Get(cardEntity));

            // Фолбэк (нет запечённого компонента) — старый путь через каталог.
            if (!_cardModelPool.Value.Has(cardEntity)) return default;
            ref var model = ref _cardModelPool.Value.Get(cardEntity);
            var instance = _cardConfig.Value.Get(model.ExpansionId, model.ModelId);
            return instance?.CardData != null ? CardVisualDataFactory.From(instance.CardData) : default;
        }
    }
}
