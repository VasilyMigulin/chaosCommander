using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // КЛАСТЕР 4 — рука/колода оппонента. «Случайная карта оппонента» собирается зон-осознанным Random-
    // таргетингом: AbilityToTarget{Random, Zone=Hand/Deck, Filters=[OpponentOwnedTargetFilter]} (выбор едет
    // в target-ключах → синк). Эти эффекты лишь воздействуют на УЖЕ выбранную карту (target).
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Сбросить цель-карту из руки в кладбище (Свежий зомби; Всадник Разложения; Расхититель).
    // target — карта в руке (собрана Random Zone=Hand + OpponentOwned). СИНК: цель в ключах, оба сбрасывают её.
    [Serializable]
    public sealed class DiscardEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.HandDisruption;
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) { UnityEngine.Debug.LogWarning("[Discard] target<0 → нет цели (Random не выбрал карту из руки оппонента?)"); return; }
            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(target)) { UnityEngine.Debug.LogWarning($"[Discard] target={target} НЕ в руке → skip"); return; }  // только из руки
            if (world.GetPool<CommanderTag>().Has(target)) { UnityEngine.Debug.Log($"[Discard] target={target} — командир, неуязвим → skip"); return; }
            if (target == cardEntity) { UnityEngine.Debug.Log($"[Discard] target={target} == источник → не сбрасываем сам себя"); return; }  // Подстава не ест себя

            // Визуал карты берём ДО снятия тегов (для анимации сброса + поп-апа). CardEntity достаточно для
            // анимации слота; Name/Icon — для будущего поп-апа «оппонент сбросил карту» у каста.
            var viewPool = world.GetPool<CardViewDataComponent>();
            string cardName = viewPool.Has(target) ? viewPool.Get(target).CardName : "";
            UnityEngine.Sprite icon = viewPool.Has(target) ? viewPool.Get(target).ArtImage : null;

            var ownerPool = world.GetPool<OwnerComponent>();
            int dbgOwner = ownerPool.Has(target) ? ownerPool.Get(target).OwnerId : -1;
            var netPool = world.GetPool<NetworkEntityComponent>();
            string dbgKey = netPool.Has(target) ? netPool.Get(target).NetworkEntityKey : "noKey";
            UnityEngine.Debug.Log($"[Discard] сбрасываю target={target} key={dbgKey} owner={dbgOwner} name='{cardName}'");

            // Стоимость сброшенной карты → контекст цепочки (Утилизация: следующая стадия наносит урон = ей).
            // Детерминировано на обоих клиентах (discard ре-ранится с тем же target-ключом) → синк даром.
            ChainContext.LastDiscardedCost = DiscardCostOf(world, target);

            handTag.Del(target);
            if (ownerPool.Has(target)) ZoneListUtil.RemoveFromHand(world, target, ownerPool.Get(target).OwnerId);

            var graveTag = world.GetPool<GraveTag>();
            if (!graveTag.Has(target)) graveTag.Add(target);

            // Анимированный сброс (slot.PlayDiscardAnimation): событие было объявлено и подписано в CardLayout,
            // но НИКТО его не публиковал → сброс не отображался. Теперь публикуем отсюда.
            GameEventBus.Publish(new CardDiscardFromHandUIEvent { CardEntity = target, CardName = cardName, Icon = icon });

            // Доменный сигнал сброса → OnDiscardTrigger сброшенной карты («Выгребной мусор»). ПОСЛЕ ухода в
            // кладбище, как CreatureDiedEvent после DeadTag. Гейт актив/пассив — внутри триггера (AbilityFire.Mark).
            GameEventBus.Publish(new CardDiscardedEvent { CardEntity = target });
        }

        // Эффективная стоимость карты (тот cost-компонент, что у неё есть).
        static int DiscardCostOf(EcsWorld world, int e)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(e)) return g.Get(e).Cost;
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(e)) return m.Get(e).Cost;
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(e)) return h.Get(e).Cost;
            return 0;
        }
    }

    // === class (OOP) === Уничтожить цель-карту ИЗ КОЛОДЫ → кладбище («Голодный повар»/«Прикурить!»:
    // «уничтожает существо в колоде»). target — карта в колоде (Random Zone=Deck + OpponentOwned +
    // CardTypeTargetFilter{Creature} по вкусу карты). Аналог DiscardEffect, но для колоды. СИНК: цель едет
    // в target-ключах, оба клиента жгут её. UI — CardMillFromDeckUIEvent (уже подписан MillNotificationView;
    // событие было объявлено, но публикатора не имело).
    [Serializable]
    public sealed class MillFromDeckEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.HandDisruption;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) { UnityEngine.Debug.LogWarning("[Mill] target<0 → нет цели (Random не выбрал карту из колоды?)"); return; }
            var deckTag = world.GetPool<DeckTag>();
            if (!deckTag.Has(target)) { UnityEngine.Debug.LogWarning($"[Mill] target={target} НЕ в колоде → skip"); return; }   // только из колоды
            if (world.GetPool<CommanderTag>().Has(target)) { UnityEngine.Debug.Log($"[Mill] target={target} — командир, неуязвим → skip"); return; }

            // Визуал ДО снятия тегов — для уведомления «карта уничтожена из колоды». Полный Visual нужен
            // владельцу: его CardLayout показывает уничтоженную карту ЦЕЛИКОМ (дуга с зависанием).
            var viewPool = world.GetPool<CardViewDataComponent>();
            string cardName = viewPool.Has(target) ? viewPool.Get(target).CardName : "";
            UnityEngine.Sprite icon = viewPool.Has(target) ? viewPool.Get(target).ArtImage : null;
            var visual = viewPool.Has(target) ? viewPool.Get(target).ToVisual() : default;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(target)) ZoneListUtil.RemoveFromDeck(world, target, ownerPool.Get(target).OwnerId);
            deckTag.Del(target);

            var graveTag = world.GetPool<GraveTag>();
            if (!graveTag.Has(target)) graveTag.Add(target);

            UnityEngine.Debug.Log($"[Mill] уничтожаю из колоды target={target} name='{cardName}'");
            GameEventBus.Publish(new CardMillFromDeckUIEvent
            {
                CardEntity   = target,
                CardName     = cardName,
                Icon         = icon,
                Visual       = visual,
                // Клиент-относительный признак «своя карта» — ровно то, что нужно UI для развилки показа.
                IsLocalOwner = world.GetPool<OwnCardTag>().Has(target),
            });
        }
    }

    // === class (OOP) === Похитить цель-карту из колоды оппонента в СВОЮ руку, удешевив на CostReduction
    // (Обнести хату). target — карта в колоде (Random Zone=Deck + OpponentOwned). Меняет владельца + теги
    // (toggle, клиент-относительно), кладёт в руку владельца источника, уменьшает кост-компонент (min 0).
    [Serializable]
    public sealed class StealToHandEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.HandDisruption;
        public int CostReduction = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var deckTag = world.GetPool<DeckTag>();
            if (!deckTag.Has(target)) return;                 // только из колоды

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(target)) return;
            int oldOwnerId = ownerPool.Get(target).OwnerId;

            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(PlayerEntity)) return;
            int newOwnerId = playerPool.Get(PlayerEntity).PlayerId;
            if (oldOwnerId == newOwnerId) return;             // уже моя

            // Лимит МОЕЙ руки (единое правило, HandSpace): места нет — кража не удаётся, карта остаётся
            // в колоде оппонента НЕТРОНУТОЙ (ничего ещё не сняли — выходим до перекладки).
            if (!HandSpace.HasRoom(world, PlayerEntity))
            {
                UnityEngine.Debug.Log($"[Steal] рука полна → кража отменена (target={target})");
                return;
            }

            ZoneListUtil.RemoveFromDeck(world, target, oldOwnerId);
            deckTag.Del(target);
            ownerPool.Get(target).OwnerId = newOwnerId;

            var ownT = world.GetPool<OwnCardTag>();
            var enemyT = world.GetPool<EnemyCardTag>();
            if (ownT.Has(target)) { ownT.Del(target); if (!enemyT.Has(target)) enemyT.Add(target); }
            else                  { if (enemyT.Has(target)) enemyT.Del(target); if (!ownT.Has(target)) ownT.Add(target); }

            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(target)) handTag.Add(target);
            ZoneListUtil.AddToHand(world, target, PlayerEntity);

            // Скидка = перманентный кост-бафф (стек кост-компонента, переживает смерть/зоны).
            if (CostReduction != 0) BuffCost.Add(world, target, -CostReduction, permanent: true);

            // ХС-семантика: способности украденной карты «служат» вору — без пере-привязки её эффекты
            // при розыгрыше кредитовали бы СТАРОГО хозяина (PlayerEntity капчурится при Init), а триггеры
            // начала/конца хода слушали бы его ход (см. AbilityOwnershipUtil).
            AbilityOwnershipUtil.Rebind(world, target, PlayerEntity);

            GameEventBus.Publish(new CardDrawnEvent { CardEntity = target, PlayerId = PlayerEntity });
        }
    }

    // === class (OOP) === Похитить цель-карту ИЗ РУКИ оппонента, замешав в СВОЮ колоду (Шулер: «посмотрите
    // 3 карты в руке противника, выберите 1»). Зеркало StealToHandEffect (Hand↔Deck поменяны местами:
    // источник — рука, назначение — колода), без скидки по стоимости (тут её не просят) — та же смена
    // владельца + тогл Own/EnemyCardTag + AbilityOwnershipUtil.Rebind (см. коммент StealToHandEffect —
    // способности украденной карты должны служить новому хозяину, а не старому).
    [Serializable]
    public sealed class StealFromHandToDeckEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.HandDisruption;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(target)) return;                              // только из руки
            if (world.GetPool<CommanderTag>().Has(target)) return;         // командир неуязвим (как Discard/Mill)

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(target)) return;
            int oldOwnerId = ownerPool.Get(target).OwnerId;

            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(PlayerEntity)) return;
            int newOwnerId = playerPool.Get(PlayerEntity).PlayerId;
            if (oldOwnerId == newOwnerId) return;                         // уже моя

            ZoneListUtil.RemoveFromHand(world, target, oldOwnerId);
            handTag.Del(target);
            ownerPool.Get(target).OwnerId = newOwnerId;

            var ownT = world.GetPool<OwnCardTag>();
            var enemyT = world.GetPool<EnemyCardTag>();
            if (ownT.Has(target)) { ownT.Del(target); if (!enemyT.Has(target)) enemyT.Add(target); }
            else                  { if (enemyT.Has(target)) enemyT.Del(target); if (!ownT.Has(target)) ownT.Add(target); }

            var deckTag = world.GetPool<DeckTag>();
            if (!deckTag.Has(target)) deckTag.Add(target);
            ZoneListUtil.AddToDeck(world, target, PlayerEntity);

            // ХС-семантика: см. StealToHandEffect — способности украденной карты теперь служат вору.
            AbilityOwnershipUtil.Rebind(world, target, PlayerEntity);
        }
    }

    // === helper === перенос карты между списками зон владельца (по PlayerId / сущности игрока).
    static class ZoneListUtil
    {
        public static void RemoveFromHand(EcsWorld world, int card, int ownerId)
        {
            var pp = world.GetPool<PlayerComponent>();
            var hp = world.GetPool<HandComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<HandComponent>().End())
            {
                if (pp.Get(pe).PlayerId != ownerId) continue;
                ref var hand = ref hp.Get(pe);
                if (hand.CardEntities != null && hand.CardEntities.Remove(card)) hand.Count = hand.CardEntities.Count;
                return;
            }
        }

        public static void RemoveFromDeck(EcsWorld world, int card, int ownerId)
        {
            var pp = world.GetPool<PlayerComponent>();
            var dp = world.GetPool<DeckComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                if (pp.Get(pe).PlayerId != ownerId) continue;
                ref var deck = ref dp.Get(pe);
                if (deck.CardEntities != null && deck.CardEntities.Remove(card)) deck.Count = deck.CardEntities.Count;
                return;
            }
        }

        public static void AddToHand(EcsWorld world, int card, int playerEntity)
        {
            var hp = world.GetPool<HandComponent>();
            if (!hp.Has(playerEntity)) return;
            ref var hand = ref hp.Get(playerEntity);
            hand.CardEntities ??= new System.Collections.Generic.List<int>();
            if (!hand.CardEntities.Contains(card)) hand.CardEntities.Add(card);
            hand.Count = hand.CardEntities.Count;
        }

        // Вставка В СЛУЧАЙНУЮ позицию (не в конец/начало) — «замешать» буквально, а не «положить сверху».
        public static void AddToDeck(EcsWorld world, int card, int playerEntity)
        {
            var dp = world.GetPool<DeckComponent>();
            if (!dp.Has(playerEntity)) return;
            ref var deck = ref dp.Get(playerEntity);
            deck.CardEntities ??= new System.Collections.Generic.List<int>();
            if (deck.CardEntities.Contains(card)) return;
            int idx = deck.CardEntities.Count == 0 ? 0 : UnityEngine.Random.Range(0, deck.CardEntities.Count + 1);
            deck.CardEntities.Insert(idx, card);
            deck.Count = deck.CardEntities.Count;
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
