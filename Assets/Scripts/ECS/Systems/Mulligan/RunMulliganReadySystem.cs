using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using Game.Core.Network;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using System.Collections.Generic;
using WebSocketSharp;
using UnityEngine;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunMulliganReadySystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsFilterInject<Inc<MulliganComponent, PlayerComponent, MulliganCompleteEvent>> _filter = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<MulliganCompleteEvent> _completePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<CardModelComponent> _cardModelPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var deckComp = ref _deckPool.Value.Get(entity);
                ref var handComp = ref _handPool.Value.Get(entity);
                ref var playerComp = ref _playerPool.Value.Get(entity);

                // PvE: снапшот/RPC не нужны (оппонент — локальный ИИ, зеркала нет). Повторяем ПРЕ-СТАРТ,
                // который в MP делает RPC_PreStartPhaseBegin: MatchStartEvent обоим игрокам + стартовая
                // рука в UI (PreStartPhaseBeginUIEvent — ЗАКРЫВАЕТ мулиган-окно и активирует CardLayout;
                // без него окно висело, а добор хода 1 бил в неактивный CardLayout — ошибка корутины).
                // Только затем — первый ход.
                if (Game.Core.Service.PveMode.Enabled)
                {
                    _mulliganPool.Value.Del(entity);
                    _completePool.Value.Del(entity);

                    var matchStartPool = _world.Value.GetPool<MatchStartEvent>();
                    foreach (var pe in _world.Value.Filter<PlayerComponent>().End())
                        if (!matchStartPool.Has(pe)) matchStartPool.Add(pe);

                    PublishPreStartHandUI(entity, ref playerComp);
                    TurnFlow.GrantTurn(_world.Value, entity, 1);
                    Debug.Log($"[RunMulliganReadySystem] PvE: пре-старт + первый ход игроку {playerComp.PlayerId}");
                    continue;
                }

                Debug.Log($"[RunMulliganReadySystem] Player {playerComp.PlayerId}: mulligan complete. Sending deck snapshot and notifying host.");

                SendSnapshotToOpponent(entity, ref playerComp);

                Debug.Log($"[RunMulliganReadySystem] Player {playerComp.PlayerId}: deck snapshot sent. Calling RPC_NotifyMulliganReady.");

                if (_photon.Value == null)
                    Debug.LogError("[RunMulliganReadySystem] PhotonRunHandler is NULL — RPC_NotifyMulliganReady will NOT be sent!");

                _photon.Value?.RPC_NotifyMulliganReady();

                Debug.Log($"[RunMulliganReadySystem] Player {playerComp.PlayerId}: RPC_NotifyMulliganReady called.");

                _mulliganPool.Value.Del(entity);
                _completePool.Value.Del(entity);
            }
        }

        // PvE-аналог PhotonRunHandler.PublishPreStartHandUI (тот приватный в сборке Photon): собирает
        // стартовую руку локального игрока в PreStartPhaseBeginUIEvent — MuliganWindow закрывается,
        // CardLayout показывает руку и командира.
        void PublishPreStartHandUI(int playerEntity, ref PlayerComponent player)
        {
            var viewPool = _world.Value.GetPool<CardViewDataComponent>();
            ref var hand = ref _handPool.Value.Get(playerEntity);

            var handCards = new List<CardAddedToHandUIEvent>();
            CardAddedToHandUIEvent commanderCard = default;
            bool hasCommander = false;

            for (int i = 0; i < hand.Count; i++)
            {
                int cardEntity = hand.CardEntities[i];
                if (!viewPool.Has(cardEntity)) continue;

                ref var view = ref viewPool.Get(cardEntity);
                string netKey = _netKeyPool.Value.Has(cardEntity) ? _netKeyPool.Value.Get(cardEntity).NetworkEntityKey : string.Empty;
                bool isCommander = _commanderPool.Value.Has(cardEntity);

                var evt = new CardAddedToHandUIEvent
                {
                    CardEntity  = cardEntity,
                    PlayerId    = player.PlayerId,
                    NetworkKey  = netKey,
                    Icon        = view.ArtImage,
                    CardType    = view.CardType,
                    Element     = view.Element,
                    Rarity      = view.Rarity,
                    CardName    = view.CardName,
                    IsCommander = isCommander,
                    Visual      = new Game.Core.Shared.CardVisualData
                    {
                        CardName    = view.CardName,
                        Description = view.Description,
                        Icon        = view.ArtImage,
                        CardType    = view.CardType,
                        Rarity      = view.Rarity,
                        Element     = view.Element,
                        CostType    = view.CostType,
                        CostAmount  = view.CostAmount,
                        IsCreature  = view.IsCreature,
                        Attack      = view.Attack,
                        MaxHealth   = view.MaxHealth,
                        Speed       = view.Speed,
                        IsCommander = isCommander,
                    },
                };

                if (isCommander) { commanderCard = evt; hasCommander = true; }
                else             { handCards.Add(evt); }
            }

            GameEventBus.Publish(new PreStartPhaseBeginUIEvent
            {
                HandCards     = handCards.ToArray(),
                CommanderCard = commanderCard,
                HasCommander  = hasCommander,
            });
        }

        void SendSnapshotToOpponent(int playerEntity, ref PlayerComponent player)
        {
            ref var deck = ref _deckPool.Value.Get(playerEntity);
            ref var hand = ref _handPool.Value.Get(playerEntity);

            var deckExpansionIds = new List<string>();
            var deckCardIds = new List<int>();
            var deckNetKeys = new List<string>();

            var handExpansionIds = new List<string>();
            var handCardIds = new List<int>();
            var handNetKeys = new List<string>();

            string commanderExpansionID = string.Empty;
            int commanderID = 0;
            string commanderNetKey = string.Empty;

            for (int i = 0; i < deck.Count; i++)
                CollectCardData(deck.CardEntities[i], deckExpansionIds, deckCardIds, deckNetKeys,
                    ref commanderExpansionID, ref commanderID, ref commanderNetKey);

            for (int i = 0; i < hand.Count; i++)
                CollectCardData(hand.CardEntities[i], handExpansionIds, handCardIds, handNetKeys,
                    ref commanderExpansionID, ref commanderID, ref commanderNetKey);
             
            var snapshot = new NetworkDeckSnapshotData();

            int deckCount = deckExpansionIds.Count;

            snapshot.Deck = new NetworkCardSnapshotEntry[deckCount];

            for (int i = 0; i < deckCount; i++)
            {
                snapshot.Deck[i] = new NetworkCardSnapshotEntry
                {
                    ExpansionId = deckExpansionIds[i],
                    CardId = deckCardIds[i],
                    EntityKey = deckNetKeys[i]
                };
            }

            snapshot.DeckCount = deckCount;

            int handCount = handExpansionIds.Count;
            snapshot.Hand = new NetworkCardSnapshotEntry[handCount];
            for (int i = 0; i < handCount; i++)
            {
                snapshot.Hand[i] = new NetworkCardSnapshotEntry
                {
                    ExpansionId = handExpansionIds[i],
                    CardId = handCardIds[i],
                    EntityKey = handNetKeys[i]
                };
            }
            snapshot.HandCount = handCount;

            // Сайдборд: своя зона, в списки колоды/руки не входит — собираем прямым обходом по тегу.
            var sideExpansionIds = new List<string>();
            var sideCardIds = new List<int>();
            var sideNetKeys = new List<string>();
            var sideboardTag = _world.Value.GetPool<SideboardTag>();
            var ownerPool = _world.Value.GetPool<OwnerComponent>();
            foreach (var e in _world.Value.Filter<SideboardTag>().Inc<OwnerComponent>().End())
            {
                if (ownerPool.Get(e).OwnerId != player.PlayerId) continue;
                if (!_netKeyPool.Value.Has(e) || !_cardModelPool.Value.Has(e)) continue;
                ref var m = ref _cardModelPool.Value.Get(e);
                sideExpansionIds.Add(m.ExpansionId ?? string.Empty);
                sideCardIds.Add(m.ModelId);
                sideNetKeys.Add(_netKeyPool.Value.Get(e).NetworkEntityKey);
            }
            snapshot.Sideboard = new NetworkCardSnapshotEntry[sideExpansionIds.Count];
            for (int i = 0; i < sideExpansionIds.Count; i++)
            {
                snapshot.Sideboard[i] = new NetworkCardSnapshotEntry
                {
                    ExpansionId = sideExpansionIds[i],
                    CardId = sideCardIds[i],
                    EntityKey = sideNetKeys[i]
                };
            }
            snapshot.SideboardCount = sideExpansionIds.Count;

            snapshot.Commander = new NetworkCardSnapshotEntry()
            {
                ExpansionId = commanderExpansionID,
                CardId = commanderID,
                EntityKey = commanderNetKey
            };

            _photon.Value?.SendDeckSnapshotChunked(MemoryPack.MemoryPackSerializer.Serialize(snapshot), player.PlayerId);
            // Уведомляем хост что мулиган завершён и снэпшот отправлен
        }

        void CollectCardData(int cardEntity, List<string> expansionIds, List<int> cardIds, List<string> netKeys,
            ref string commanderExpansionID, ref int commanderID, ref string commanderNetKey)
        {
            if (!_netKeyPool.Value.Has(cardEntity) || !_cardModelPool.Value.Has(cardEntity))
                return;

            if (_commanderPool.Value.Has(cardEntity))
            {
                ref var model = ref _cardModelPool.Value.Get(cardEntity);
                commanderExpansionID = model.ExpansionId ?? string.Empty;
                commanderID = model.ModelId;
                commanderNetKey = _netKeyPool.Value.Get(cardEntity).NetworkEntityKey;
                return;
            }
            else
            { 
                ref var model = ref _cardModelPool.Value.Get(cardEntity);
                expansionIds.Add(model.ExpansionId ?? string.Empty);
                cardIds.Add(model.ModelId);
                netKeys.Add(_netKeyPool.Value.Get(cardEntity).NetworkEntityKey);
            }
        }
    }
}