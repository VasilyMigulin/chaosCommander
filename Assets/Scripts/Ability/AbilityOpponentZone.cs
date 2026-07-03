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

            handTag.Del(target);
            if (ownerPool.Has(target)) ZoneListUtil.RemoveFromHand(world, target, ownerPool.Get(target).OwnerId);

            var graveTag = world.GetPool<GraveTag>();
            if (!graveTag.Has(target)) graveTag.Add(target);

            // Анимированный сброс (slot.PlayDiscardAnimation): событие было объявлено и подписано в CardLayout,
            // но НИКТО его не публиковал → сброс не отображался. Теперь публикуем отсюда.
            GameEventBus.Publish(new CardDiscardFromHandUIEvent { CardEntity = target, CardName = cardName, Icon = icon });
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

            if (CostReduction != 0) ReduceCost(world, target, CostReduction);

            GameEventBus.Publish(new CardDrawnEvent { CardEntity = target, PlayerId = PlayerEntity });
        }

        static void ReduceCost(EcsWorld world, int card, int amount)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { ref var c = ref g.Get(card); c.Cost = Math.Max(0, c.Cost - amount); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { ref var c = ref m.Get(card); c.Cost = Math.Max(0, c.Cost - amount); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { ref var c = ref h.Get(card); c.Cost = Math.Max(0, c.Cost - amount); }
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

        public static int FindPlayerEntity(EcsWorld world, int ownerId)
        {
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
                if (pp.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }
    }
}
