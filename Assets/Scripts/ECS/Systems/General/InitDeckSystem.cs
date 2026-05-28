using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Инициализирует колоду локального игрока из DeckStorage.
    /// Публикует CreateCardEvent для каждой карты колоды — их подхватывает CreateCardSystem.
    /// Запускается после InitLocalPlayerSystem (как IEcsInitSystem).
    /// </summary>
    public sealed class InitDeckSystem : IEcsInitSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsFilterInject<Inc<PlayerComponent, DeckComponent, HandComponent, LocalComponent>> _playerFilter = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;

        public void Init(IEcsSystems systems)
        {
            var decks = DeckStorage.GetCached();
            if (decks == null || decks.Count == 0)
            {
                Debug.LogWarning("[InitDeckSystem] No saved decks found in DeckStorage. Using empty deck.");
                return;
            }

            // Берём первую доступную колоду
            var savedDeck = decks[0];

            foreach (var playerEntity in _playerFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(playerEntity);
                if (!player.IsLocalPlayer)
                    continue;

                var cardEntities = new List<int>();
                int commanderEntity = -1;

                // Commander — создаём отдельно, в руку не кладём пока
                if (savedDeck.Commander.CardId != 0)
                {
                    commanderEntity = CreateCardEntity(savedDeck.Commander.ExpansionId, savedDeck.Commander.CardId, player.PlayerId, playerEntity, isCommander: true);
                }

                // Остальные карты → в колоду
                foreach (var card in savedDeck.Cards)
                {
                    int copies = Mathf.Max(1, card.Count);
                    for (int i = 0; i < copies; i++)
                    {
                        int newCardEntity = CreateCardEntity(card.ExpansionId, card.CardId, player.PlayerId, playerEntity, isCommander: false);
                        if (newCardEntity >= 0) cardEntities.Add(newCardEntity);
                    }
                }

                cardEntities.Shuffle();

                ref var deck = ref _deckPool.Value.Get(playerEntity);
                deck.CardEntities = cardEntities;
                deck.Count = cardEntities.Count;

                // Командир всегда в руке на индексе 0
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (commanderEntity >= 0)
                {
                    hand.CardEntities.Insert(0, commanderEntity);
                    hand.Count = hand.CardEntities.Count;

                    // Перевешиваем теги: командир — в руке, не в колоде
                    if (_deckTagPool.Value.Has(commanderEntity))
                        _deckTagPool.Value.Del(commanderEntity);
                    if (!_handTagPool.Value.Has(commanderEntity))
                        _handTagPool.Value.Add(commanderEntity);
                }

                Debug.Log($"[InitDeckSystem] Player {player.PlayerId}: deck={deck.Count} cards, commander in hand at index 0 ('{savedDeck.Name}')");
            }
        }

        private int CreateCardEntity(string expansionId, int cardId, int ownerId, int playerEntity, bool isCommander)
        {
            var instance = _cardConfig.Value.Get(expansionId, cardId);
            if (instance?.CardData == null)
            {
                Debug.LogWarning($"[InitDeckSystem] Card not found: expansion='{expansionId}' cardId={cardId}");
                return -1;
            }

            var world = _world.Value;
            int cardEntity = instance.CardData.InitAndGetEntity(world, isCommander);

            string netKey = Guid.NewGuid().ToString();

            if (!_netKeyPool.Value.Has(cardEntity))
            {
                ref var net = ref _netKeyPool.Value.Add(cardEntity);
                net.NetworkEntityKey = netKey;
            }

            // Командир изначально не идёт в колоду — тег выставит логика выше
            if (!isCommander && !_deckTagPool.Value.Has(cardEntity))
                _deckTagPool.Value.Add(cardEntity);

            if (!_ownerPool.Value.Has(cardEntity))
            {
                ref var owner = ref _ownerPool.Value.Add(cardEntity);
                owner.OwnerId = ownerId;
                owner.EntityKey = netKey;
            }

            if (!_ownTagPool.Value.Has(cardEntity))
                _ownTagPool.Value.Add(cardEntity);

            _state.Value.AddEntity(cardEntity, localKey: cardEntity.ToString(), networkKey: netKey);

            return cardEntity;
        } 
    }
}