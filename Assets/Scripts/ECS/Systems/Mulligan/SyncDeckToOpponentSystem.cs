using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// После завершения мулигана:
    ///   — собирает снэпшот своей колоды (ExpansionId + CardId + EntityKey)
    ///     и публикует DeckReadyToSyncEvent для отправки оппоненту через Photon RPC.
    ///   — при получении снэпшота оппонента (OpponentDeckSyncComponent) публикует
    ///     CreateCardEvent на каждую карту — CreateCardSystem создаёт entity.
    /// </summary>
    public sealed class SyncDeckToOpponentSystem : IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<CardModelComponent> _cardModelPool = default;
        readonly EcsPoolInject<OpponentDeckSyncComponent> _syncPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, DeckComponent>> _playerFilter = default;
        readonly EcsFilterInject<Inc<OpponentDeckSyncComponent, PlayerComponent>> _syncFilter = default;

        bool _mulliganCompleted;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<AllMulligansCompletedEvent>(OnAllMulligansCompleted);
        }

        void OnAllMulligansCompleted(AllMulligansCompletedEvent _) => _mulliganCompleted = true;

        public void Run(IEcsSystems systems)
        {
            // Получили снэпшот от оппонента — публикуем CreateCardEvent на каждую карту
            foreach (var opponentEntity in _syncFilter.Value)
            {
                ref var sync   = ref _syncPool.Value.Get(opponentEntity);
                ref var player = ref _playerPool.Value.Get(opponentEntity);

                for (int i = 0; i < sync.DeckCount; i++)
                {
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId = sync.DeckExpansionIds[i],
                        CardId      = sync.DeckCardIds[i],
                        EntityKey   = sync.DeckNetworkKeys[i],
                        OwnerId     = player.PlayerId,
                        IsEnemy     = true,
                    });
                }

                for (int i = 0; i < sync.HandCount; i++)
                {
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId = sync.HandExpansionIds[i],
                        CardId      = sync.HandCardIds[i],
                        EntityKey   = sync.HandNetworkKeys[i],
                        OwnerId     = player.PlayerId,
                        IsEnemy     = true,
                    });
                }

                _syncPool.Value.Del(opponentEntity);

                GameEventBus.Publish(new DeckSyncedEvent { PlayerEntity = opponentEntity });
                Debug.Log($"[SyncDeckToOpponentSystem] Queued {sync.DeckCount} deck + {sync.HandCount} hand CreateCardEvents for opponent player {player.PlayerId}");
            }

            // После мулигана отправляем свой снэпшот оппоненту
            if (!_mulliganCompleted) return;
            _mulliganCompleted = false;

            foreach (var playerEntity in _playerFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(playerEntity);
                if (!player.IsLocalPlayer) continue;

                SendSnapshotToOpponent(playerEntity, ref player);
            }
        }

        void SendSnapshotToOpponent(int playerEntity, ref PlayerComponent player)
        {
            ref var deck = ref _deckPool.Value.Get(playerEntity);
            ref var hand = ref _handPool.Value.Get(playerEntity);

            var deckExpansionIds = new List<string>();
            var deckCardIds      = new List<int>();
            var deckNetKeys      = new List<string>();

            var handExpansionIds = new List<string>();
            var handCardIds      = new List<int>();
            var handNetKeys      = new List<string>();

            for (int i = 0; i < deck.Count; i++)
                CollectCardData(deck.CardEntities[i], deckExpansionIds, deckCardIds, deckNetKeys);

            for (int i = 0; i < hand.Count; i++)
                CollectCardData(hand.CardEntities[i], handExpansionIds, handCardIds, handNetKeys);

            GameEventBus.Publish(new DeckReadyToSyncEvent
            {
                PlayerEntity     = playerEntity,
                PlayerId         = player.PlayerId,
                DeckExpansionIds = deckExpansionIds.ToArray(),
                DeckCardIds      = deckCardIds.ToArray(),
                DeckNetworkKeys  = deckNetKeys.ToArray(),
                HandExpansionIds = handExpansionIds.ToArray(),
                HandCardIds      = handCardIds.ToArray(),
                HandNetworkKeys  = handNetKeys.ToArray(),
            });
        }

        void CollectCardData(int cardEntity, List<string> expansionIds, List<int> cardIds, List<string> netKeys)
        {
            if (!_netKeyPool.Value.Has(cardEntity) || !_cardModelPool.Value.Has(cardEntity))
                return;

            ref var model = ref _cardModelPool.Value.Get(cardEntity);
            expansionIds.Add(model.ExpansionId ?? string.Empty);
            cardIds.Add(model.ModelId);
            netKeys.Add(_netKeyPool.Value.Get(cardEntity).NetworkEntityKey);
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<AllMulligansCompletedEvent>(OnAllMulligansCompleted);
        }
    }
}
