using System.Collections.Generic;
using Leopotam.EcsLite;
using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Model.Card
{
    /// <summary>
    /// Инициализирует колоду локального игрока: CardModel → ECS entity.
    /// Для карт оппонента используется CreateCardSystem (через CreateCardEvent).
    /// </summary>
    public static class DeckInitializer
    {
        /// <summary>
        /// Создаёт ECS entity для каждой карты, назначает NetworkEntityKey,
        /// DeckTag, OwnerComponent и OwnCardTag. Возвращает перемешанный массив.
        /// </summary>
        public static int[] BuildAndShuffle(IReadOnlyList<CardModel> cards, int ownerId, EcsWorld world)
        {
            var entities    = new List<int>(cards.Count);
            var netKeyPool  = world.GetPool<NetworkEntityComponent>();
            var deckTagPool = world.GetPool<DeckTag>();
            var ownerPool   = world.GetPool<OwnerComponent>();
            var ownTagPool  = world.GetPool<OwnCardTag>();

            for (int i = 0; i < cards.Count; i++)
            {
                var model = cards[i];
                if (model == null)
                {
                    Debug.LogWarning($"[DeckInitializer] Null card at index {i} for player {ownerId}, skipping");
                    continue;
                }

                string netKey    = $"p{ownerId}_card_{i}";
                int    cardEntity = model.InitAndGetEntity(world);

                if (!netKeyPool.Has(cardEntity))
                {
                    ref var net = ref netKeyPool.Add(cardEntity);
                    net.NetworkEntityKey = netKey;
                }

                if (!deckTagPool.Has(cardEntity))
                    deckTagPool.Add(cardEntity);

                if (!ownerPool.Has(cardEntity))
                {
                    ref var owner = ref ownerPool.Add(cardEntity);
                    owner.OwnerId  = ownerId;
                    owner.EntityKey = netKey;
                }

                if (!ownTagPool.Has(cardEntity))
                    ownTagPool.Add(cardEntity);

                entities.Add(cardEntity);
            }

            Shuffle(entities);
            return entities.ToArray();
        }

        static void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
