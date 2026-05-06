using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Предлагает карты игроку на мулиган:
    ///   - Игрок 1 (side=1): 3 карты, 1 замена.
    ///   - Игрок 2 (side=2): 4 карты, 2 замены.
    /// Срабатывает один раз когда колода проинициализирована (DeckConfig снят)
    /// и MulliganComponent ещё не добавлен.
    /// </summary>
    public sealed class MulliganOfferSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsFilterInject<Inc<DeckComponent, HandComponent, PlayerComponent, PlayerSideComponent>, Exc<MulliganComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var playerEntity in _filter.Value)
            {
                ref var side = ref _sidePool.Value.Get(playerEntity);
                int offerCount = side.Side == 1 ? 3 : 4;
                int maxReplacements = side.Side == 1 ? 1 : 2;

                ref var deck = ref _deckPool.Value.Get(playerEntity);
                ref var hand = ref _handPool.Value.Get(playerEntity);
                ref var player = ref _playerPool.Value.Get(playerEntity);

                if (deck.Count < offerCount)
                {
                    Debug.LogWarning($"[MulliganOfferSystem] Player {player.PlayerId}: not enough cards in deck ({deck.Count} < {offerCount})");
                    offerCount = deck.Count;
                }

                var offeredCards = new List<int>(offerCount);

                // Берём карты с вершины колоды в руку
                for (int i = 0; i < offerCount; i++)
                {
                    int cardEntity = deck.CardEntities[deck.Count - 1 - i];
                    offeredCards.Add(cardEntity);

                    // Переводим карту из колоды в руку
                    if (_deckTagPool.Value.Has(cardEntity))
                        _deckTagPool.Value.Del(cardEntity);

                    if (!_handTagPool.Value.Has(cardEntity))
                        _handTagPool.Value.Add(cardEntity);

                    hand.CardEntities[hand.Count + i] = cardEntity;
                }

                hand.Count += offerCount;
                deck.Count -= offerCount;

                ref var mulligan = ref _mulliganPool.Value.Add(playerEntity);
                mulligan.Phase = MulliganPhase.Offering;
                mulligan.OfferedCards = offeredCards;
                mulligan.MaxReplacements = maxReplacements;
                mulligan.ReplacementsUsed = 0;

                GameEventBus.Publish(new MulliganStartedEvent
                {
                    PlayerEntity = playerEntity,
                    OfferedCardEntities = offeredCards.ToArray(),
                    MaxReplacements = maxReplacements
                });

                Debug.Log($"[MulliganOfferSystem] Player {player.PlayerId} side={side.Side}: offered {offerCount} cards, {maxReplacements} replacements allowed");
            }
        }
    }
}
