using System.Collections.Generic;
using Leopotam.EcsLite;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Instance.Card;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Общие хелперы для механики раскопки (CardPick). Используются и
    /// CardPickSelectionSystem (пре-каст пик через PlayRequirement), и
    /// ApplyPickCardSystem (мид-цепочечный PickCardEffect).
    /// </summary>
    public static class CardPickHelper
    {
        /// <summary>
        /// Собрать предложение карт из указанного источника для игрока.
        /// Возвращает массив entity-кандидатов (длина ≤ offerCount).
        /// </summary>
        public static int[] CollectOffered(EcsWorld world, CardPickSourceType source,
                                           int offerCount, int[] uniquePoolModelIds,
                                           int playerEntity)
        {
            int activePlayerId = world.GetPool<PlayerComponent>().Get(playerEntity).PlayerId;

            switch (source)
            {
                case CardPickSourceType.PlayerDeck:    return TakeFromDeck(world, activePlayerId, ownedByPlayer: true,  offerCount);
                case CardPickSourceType.OpponentDeck:  return TakeFromDeck(world, activePlayerId, ownedByPlayer: false, offerCount);
                case CardPickSourceType.PlayerHand:    return TakeFromHand(world, activePlayerId, ownedByPlayer: true,  offerCount);
                case CardPickSourceType.OpponentHand:  return TakeFromHand(world, activePlayerId, ownedByPlayer: false, offerCount);
                case CardPickSourceType.PlayerGrave:   return TakeFromGrave(world, activePlayerId, ownedByPlayer: true,  offerCount);
                case CardPickSourceType.OpponentGrave: return TakeFromGrave(world, activePlayerId, ownedByPlayer: false, offerCount);
                case CardPickSourceType.UniquePool:    return TakeFromUniquePool(world, uniquePoolModelIds, offerCount);
                default:                               return System.Array.Empty<int>();
            }
        }

        /// <summary>Сборка визуальных данных для UI (по списку entity).</summary>
        public static CardVisualData[] BuildVisuals(EcsWorld world, CardConfig cardConfig, int[] cardEntities)
        {
            var modelPool = world.GetPool<CardModelComponent>();
            var result = new CardVisualData[cardEntities.Length];
            for (int i = 0; i < cardEntities.Length; i++)
            {
                int e = cardEntities[i];
                if (!modelPool.Has(e)) { result[i] = default; continue; }
                ref var m = ref modelPool.Get(e);
                var instance = cardConfig.Get(m.ExpansionId, m.ModelId);
                result[i] = instance?.CardData != null
                    ? CardVisualDataFactory.From(instance.CardData)
                    : default;
            }
            return result;
        }

        // ── Источники ────────────────────────────────────────────────────────

        static int[] TakeFromDeck(EcsWorld world, int activePlayerId, bool ownedByPlayer, int count)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var deckPool   = world.GetPool<DeckComponent>();
            var result = new List<int>(count);

            foreach (var entity in world.Filter<DeckComponent>().Inc<PlayerComponent>().End())
            {
                ref var pl = ref playerPool.Get(entity);
                bool isOwn = pl.PlayerId == activePlayerId;
                if (isOwn != ownedByPlayer) continue;

                ref var deck = ref deckPool.Get(entity);
                for (int i = 0; i < deck.Count && result.Count < count; i++)
                    result.Add(deck.CardEntities[i]);
                break;
            }
            return result.ToArray();
        }

        static int[] TakeFromHand(EcsWorld world, int activePlayerId, bool ownedByPlayer, int count)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            var candidates = new List<int>();
            foreach (var ce in world.Filter<HandTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().End())
            {
                bool isOwn = ownerPool.Get(ce).OwnerId == activePlayerId;
                if (isOwn == ownedByPlayer) candidates.Add(ce);
            }
            return PickRandom(candidates, count);
        }

        static int[] TakeFromGrave(EcsWorld world, int activePlayerId, bool ownedByPlayer, int count)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            var candidates = new List<int>();
            foreach (var ce in world.Filter<GraveTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().End())
            {
                bool isOwn = ownerPool.Get(ce).OwnerId == activePlayerId;
                if (isOwn == ownedByPlayer) candidates.Add(ce);
            }
            return PickRandom(candidates, count);
        }

        static int[] TakeFromUniquePool(EcsWorld world, int[] modelIds, int count)
        {
            if (modelIds == null || modelIds.Length == 0) return System.Array.Empty<int>();

            var modelPool = world.GetPool<CardModelComponent>();
            var candidates = new List<int>();
            foreach (var ce in world.Filter<CardModelComponent>().End())
            {
                int mid = modelPool.Get(ce).ModelId;
                for (int i = 0; i < modelIds.Length; i++)
                {
                    if (modelIds[i] == mid) { candidates.Add(ce); break; }
                }
            }
            return PickRandom(candidates, count);
        }

        static int[] PickRandom(List<int> source, int count)
        {
            if (source.Count == 0) return System.Array.Empty<int>();

            for (int i = source.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = source[i]; source[i] = source[j]; source[j] = tmp;
            }

            int take = System.Math.Min(count, source.Count);
            var res = new int[take];
            for (int i = 0; i < take; i++) res[i] = source[i];
            return res;
        }
    }
}
