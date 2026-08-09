using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    // === helper (static) ===
    /// <summary>
    /// ЕДИНАЯ проверка «влезет ли ещё одна карта в руку» (лимит HandComponent.MaxNonCommanderCards; у
    /// командира отдельный слот, он в лимит не входит и всегда возвращается).
    ///
    /// ЗАЧЕМ: путей «карта → в руку» больше десятка (добор, баунс, генерация, кража, дискавер, замена
    /// добора, копии в руку), и каждый считал лимит сам либо не считал вовсе — отсюда семья багов
    /// «7 из 6» и «рука полна при пустых слотах» (2026-07-30/31). Теперь правило одно и живёт здесь.
    ///
    /// Считаем по ФАКТИЧЕСКОМУ списку (не по hand.Count): при пачке (Ураган, мульти-генерация) каждая
    /// следующая карта видит актуальную заполненность, а рассинхрон счётчика не даёт ложного «место есть».
    ///
    /// НЕ ГЕЙТИТЬ этим: реплей у пассива и ресинк (ReplayActionSystem/WorldResyncSystem) — они
    /// авторитетно повторяют решения активного клиента, любая своя проверка там = рассинхрон.
    /// </summary>
    public static class HandSpace
    {
        /// <summary>Есть ли место под обычную (не-командирскую) карту в руке игрока-сущности.</summary>
        public static bool HasRoom(EcsWorld world, int playerEntity)
        {
            if (playerEntity < 0) return false;
            var handPool = world.GetPool<HandComponent>();
            if (!handPool.Has(playerEntity)) return false;

            ref var hand = ref handPool.Get(playerEntity);
            if (hand.CardEntities == null) return true;

            var commanderPool = world.GetPool<CommanderTag>();
            var handTagPool   = world.GetPool<HandTag>();
            var toGravePool   = world.GetPool<MoveCardToGraveEvent>();
            var toBoardPool   = world.GetPool<MoveCardToBoardEvent>();

            int occupied = 0;
            foreach (var card in hand.CardEntities)
            {
                if (commanderPool.Has(card)) continue;   // у командира свой слот, в лимит не входит

                // Карта, которая УЖЕ ПОКИДАЕТ руку, места не занимает. Иначе «разыграл карту-переносчик
                // при полной руке → принесённая карта сгорала», хотя слот освобождается этим же розыгрышем:
                // роутер помечает карту на уход (MoveCardToGrave/MoveCardToBoard) в _abilitySystems, а
                // физически из списка её убирают позже в кадре (_creatureSystems) — эффект успевал увидеть
                // руку «полной». Плюс HandTag уже снят у карт, уведённых напрямую (PlayCardUtil).
                if (!handTagPool.Has(card)) continue;
                if (toGravePool.Has(card) || toBoardPool.Has(card)) continue;

                occupied++;
            }

            return occupied < HandComponent.MaxNonCommanderCards;
        }

        /// <summary>То же по PlayerId владельца (карты знают ownerId, а не сущность игрока).</summary>
        public static bool HasRoomForOwner(EcsWorld world, int ownerId)
            => HasRoom(world, FindPlayerEntity(world, ownerId));

        /// <summary>Карта, которой не нашлось места, «сгорает»: токен — в лимбо, обычная — в кладбище.
        /// Зональные теги руки/колоды/борда вызывающий снимает сам (он знает, откуда карта шла).</summary>
        public static void Burn(EcsWorld world, int card, string reason)
        {
            var model = world.GetPool<CardModelComponent>();
            string name = model.Has(card) ? model.Get(card).CardName : "?";
            UnityEngine.Debug.Log($"[HandSpace] рука полна → '{name}' (entity={card}) сгорает: {reason}");

            if (world.GetPool<TokenTag>().Has(card))
            {
                var limbo = world.GetPool<LimboTag>();
                if (!limbo.Has(card)) limbo.Add(card);
                return;
            }
            var grave = world.GetPool<GraveTag>();
            if (!grave.Has(card)) grave.Add(card);
        }

        public static int FindPlayerEntity(EcsWorld world, int ownerId)
        {
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
                if (pp.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }
    }
}
