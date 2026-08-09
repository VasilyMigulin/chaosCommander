using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
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
        readonly EcsPoolInject<SideboardTag> _sideboardTagPool = default;   // «отложенные» (Сказочник)
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;

        public void Init(IEcsSystems systems)
        {
            // СЮЖЕТ (PvE): энкаунтер может навязать игроку сюжетную колоду (стори-командир + карты) —
            // тогда DeckStorage игнорируется. Пусто → играем своей колодой, как обычно.
            Game.Core.Configs.DeckPreset storyDeck = null;
            if (Game.Core.Service.PveMode.Enabled)
            {
                var enc = Game.Core.Configs.PveEncounterLocator.Current;
                if (enc != null && enc.PlayerDeck != null)
                {
                    storyDeck = enc.PlayerDeck;
                    Debug.Log($"[InitDeckSystem] PvE: сюжетная колода игрока '{storyDeck.DeckName}' (из энкаунтера)");
                }
            }

            // Колода, которую игрок ВЫБРАЛ в DeckViewPanel (DeckStorage.ActiveDeckName). Если не выбирал —
            // GetActive() отдаст первую. Раньше здесь брался decks[0] вслепую, и выбор колоды был невозможен.
            var savedDeck = storyDeck == null ? DeckStorage.GetActive() : null;
            if (storyDeck == null && savedDeck == null)
            {
                Debug.LogWarning("[InitDeckSystem] No saved decks found in DeckStorage. Using empty deck.");
                return;
            }
            if (savedDeck != null)
                Debug.Log($"[InitDeckSystem] Играем колодой '{savedDeck.Name}'.");

            foreach (var playerEntity in _playerFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(playerEntity);
                if (!player.IsLocalPlayer)
                    continue;

                var cardEntities = new List<int>();
                int commanderEntity = -1;

                if (storyDeck != null)
                {
                    // ── Сюжетная колода из DeckPreset ── ПРЯМОЙ CardData (стори-карты вне экспаншенов,
                    // config.Get их не резолвит — см. перегрузку CreateCardEntity(CardModel)).
                    if (storyDeck.Commander != null && storyDeck.Commander.CardData != null)
                        commanderEntity = CreateCardEntity(storyDeck.Commander.CardData, player.PlayerId, playerEntity, isCommander: true);

                    foreach (var entry in storyDeck.Cards)
                    {
                        if (entry.Card == null || entry.Card.CardData == null) continue;
                        int copies = Mathf.Max(1, entry.Count);
                        for (int i = 0; i < copies; i++)
                        {
                            int e = CreateCardEntity(entry.Card.CardData, player.PlayerId, playerEntity, isCommander: false);
                            if (e >= 0) cardEntities.Add(e);
                        }
                    }
                }
                else
                {
                    // ── Обычный путь: колода из DeckStorage ──
                    // Commander — создаём отдельно, в руку не кладём пока
                    // IsSet, а НЕ CardId != 0: ноль — валидный Id (Шальной принц stolen_princess/0), и
                    // старая проверка молча оставляла игрока без командира (см. OwnedCardData.IsSet).
                    if (savedDeck.Commander.IsSet)
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

                // ── Сайдборд («отложенные», Сказочник) ──
                // Создаём ПОСЛЕ Shuffle и вне cardEntities: эти карты не в колоде и не в руке, они лежат
                // в своей зоне (SideboardTag) и не участвуют ни в доборе, ни в мулигане. Достаёт их
                // способность носителя (раскопка с TargetZone.Sideboard).
                int sideboardCount = 0;
                if (storyDeck == null && savedDeck?.Sideboard != null)
                {
                    foreach (var card in savedDeck.Sideboard)
                    {
                        int copies = Mathf.Max(1, card.Count);
                        for (int i = 0; i < copies; i++)
                        {
                            int e = CreateCardEntity(card.ExpansionId, card.CardId, player.PlayerId, playerEntity, isCommander: false);
                            if (e < 0) continue;

                            // CreateCardEntity по умолчанию кладёт карту в КОЛОДУ — перевешиваем зону.
                            if (_deckTagPool.Value.Has(e)) _deckTagPool.Value.Del(e);
                            if (!_sideboardTagPool.Value.Has(e)) _sideboardTagPool.Value.Add(e);
                            sideboardCount++;
                        }
                    }
                }

                string deckName = storyDeck != null ? storyDeck.DeckName : savedDeck.Name;
                if (sideboardCount > 0)
                    Debug.Log($"[InitDeckSystem] Player {player.PlayerId}: отложено (сайдборд) {sideboardCount} карт");
                // Лог ЧЕСТНЫЙ про командира: прежний писал «commander in hand at index 0» ВСЕГДА, даже когда
                // командира не создали, а в скобках печаталось имя КОЛОДЫ — выглядело как имя карты. Из-за
                // этого матч без командира читался в логах как исправный (2026-08-01).
                if (commanderEntity >= 0)
                    Debug.Log($"[InitDeckSystem] Player {player.PlayerId}: колода '{deckName}', deck={deck.Count} карт, командир в руке (entity={commanderEntity})");
                else
                    Debug.LogError($"[InitDeckSystem] Player {player.PlayerId}: колода '{deckName}', deck={deck.Count} карт, КОМАНДИР НЕ СОЗДАН — "
                                 + $"матч без командира (ссылка пуста или карта не найдена в каталоге: "
                                 + $"exp='{savedDeck?.Commander.ExpansionId}' id={savedDeck?.Commander.CardId})");
            }
        }

        // Резолв по (ExpansionId, CardId) через каталог — для колоды из DeckStorage (там хранятся только id).
        private int CreateCardEntity(string expansionId, int cardId, int ownerId, int playerEntity, bool isCommander)
        {
            var instance = _cardConfig.Value.Get(expansionId, cardId);
            if (instance?.CardData == null)
            {
                Debug.LogWarning($"[InitDeckSystem] Card not found: expansion='{expansionId}' cardId={cardId}");
                return -1;
            }
            return CreateCardEntity(instance.CardData, ownerId, playerEntity, isCommander);
        }

        // Прямой путь по модели — для СЮЖЕТНОЙ колоды из DeckPreset: стори-карты лежат ВНЕ экспаншенов
        // (config.Get их не найдёт → колода собиралась пустой), поэтому берём CardData из пресета напрямую,
        // как InitPveOpponentSystem строит колоду ИИ. Только PvE (storyDeck-ветка) — синк не задействован.
        private int CreateCardEntity(Game.Core.Model.Card.CardModel model, int ownerId, int playerEntity, bool isCommander)
        {
            if (model == null) return -1;

            var world = _world.Value;
            int cardEntity = model.Init(world, playerEntity, isCommander);

            string netKey = NetKey.Next();   // короткий "{playerId}-{N}" вместо 36-симв. Guid (лимит RPC)

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