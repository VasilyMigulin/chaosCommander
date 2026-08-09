using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// Исполнение АЛЬТЕРНАТИВНОЙ УПЛАТЫ (семейство «Бесчестный букмекер», AltCostComponent): общие
    /// операции для актива (RunCastRouterSystem: роллит жертву и исполняет) и пассива (ReplayActionSystem:
    /// исполняет по ключам из ActionCastData.AltPaidKeys). Перекладки повторяют семантику одноимённых
    /// эффектов 1:1 (те же события: CardDiscardedEvent → OnDiscard-триггеры/Подписка РАБОТАЮТ на уплате
    /// сбросом — синергия архетипов задумана).
    /// </summary>
    public static class AltCostUtil
    {
        /// <summary>Урон себе (DamageSelf): штатный TakeDamageEvent → Вуду-редирект/пейн-триггеры работают.</summary>
        public static void DamageSelf(EcsWorld world, int playerEntity, int sourceCard, int amount)
        {
            if (playerEntity < 0 || amount <= 0) return;
            var dmgPool = world.GetPool<TakeDamageEvent>();
            if (!dmgPool.Has(playerEntity)) dmgPool.Add(playerEntity);
            ref var d = ref dmgPool.Get(playerEntity);
            d.Amount += amount;
            d.Attacker = sourceCard;
        }

        /// <summary>Есть ли ЧЕМ платить (это СТОИМОСТЬ: нет жертвы → каст невозможен, как без маны).
        /// DamageSelf всегда оплатим (суицид разрешён по дизайну, HP не гейтим; кост 0 = урон 0).
        /// Без роллов (Random не дёргается — чек зовёт и подсветка руки).</summary>
        public static bool CanPay(EcsWorld world, AltCostKind kind, int ownerId, int excludeCard)
            => kind == AltCostKind.DamageSelf || Candidates(world, kind, ownerId, excludeCard).Count > 0;

        // ── Роллы жертв (ТОЛЬКО актив: выбор недетерминирован, пассиву едет ключом) ──

        public static int RollOwnHandCard(EcsWorld world, int ownerId, int exclude)
            => Roll(Candidates(world, AltCostKind.DiscardHand, ownerId, exclude));

        public static int RollOwnBoardCreature(EcsWorld world, int ownerId)
            => Roll(Candidates(world, AltCostKind.SacrificeCreature, ownerId, -1));

        public static int RollOwnDeckCard(EcsWorld world, int ownerId)
            => Roll(Candidates(world, AltCostKind.MillDeck, ownerId, -1));

        static int Roll(System.Collections.Generic.List<int> candidates)
            => candidates.Count == 0 ? -1 : candidates[UnityEngine.Random.Range(0, candidates.Count)];

        static System.Collections.Generic.List<int> Candidates(EcsWorld world, AltCostKind kind, int ownerId, int exclude)
        {
            EcsFilter filter = kind switch
            {
                AltCostKind.DiscardHand       => world.Filter<HandTag>().Inc<OwnerComponent>().End(),
                AltCostKind.SacrificeCreature => world.Filter<CreatureTag>().Inc<BoardTag>().Inc<OwnerComponent>().Exc<DeadTag>().End(),
                _                             => world.Filter<DeckTag>().Inc<OwnerComponent>().End(),   // MillDeck
            };
            var owner = world.GetPool<OwnerComponent>();
            var commander = world.GetPool<CommanderTag>();
            var candidates = new System.Collections.Generic.List<int>();
            foreach (var e in filter)
            {
                if (e == exclude) continue;
                if (owner.Get(e).OwnerId != ownerId) continue;
                if (commander.Has(e)) continue;   // командир неуязвим к уплате (как к сбросу/миллу)
                candidates.Add(e);
            }
            return candidates;
        }

        // ── Перекладки (обе стороны; семантика = DiscardEffect / DestroyEffect / MillFromDeckEffect) ──

        /// <summary>Сброс конкретной карты руки в кладбище (уплата DiscardHand).</summary>
        public static void Discard(EcsWorld world, int victim)
        {
            var handTag = world.GetPool<HandTag>();
            if (victim < 0 || !handTag.Has(victim)) return;

            var viewPool = world.GetPool<CardViewDataComponent>();
            string cardName = viewPool.Has(victim) ? viewPool.Get(victim).CardName : "";
            UnityEngine.Sprite icon = viewPool.Has(victim) ? viewPool.Get(victim).ArtImage : null;

            handTag.Del(victim);
            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(victim)) RemoveFromHandList(world, victim, ownerPool.Get(victim).OwnerId);

            var graveTag = world.GetPool<GraveTag>();
            if (!graveTag.Has(victim)) graveTag.Add(victim);

            GameEventBus.Publish(new CardDiscardFromHandUIEvent { CardEntity = victim, CardName = cardName, Icon = icon });
            GameEventBus.Publish(new CardDiscardedEvent { CardEntity = victim });   // OnDiscard-триггеры работают
        }

        /// <summary>Жертва существа (SacrificeCreature): DeadTag → DieSystem. KilledBy НЕ ставим —
        /// жертва СВОЯ, мана за килл не положена (и гейт killer!=owner всё равно бы отсёк).</summary>
        public static void Sacrifice(EcsWorld world, int victim)
        {
            var creature = world.GetPool<CreatureTag>();
            var dead = world.GetPool<DeadTag>();
            if (victim < 0 || !creature.Has(victim) || dead.Has(victim)) return;
            var hpPool = world.GetPool<HealthComponent>();
            if (hpPool.Has(victim)) hpPool.Get(victim).Current = 0;
            dead.Add(victim);
        }

        /// <summary>Уничтожение конкретной карты колоды (MillDeck) → кладбище.</summary>
        public static void Mill(EcsWorld world, int victim)
        {
            var deckTag = world.GetPool<DeckTag>();
            if (victim < 0 || !deckTag.Has(victim)) return;

            var viewPool = world.GetPool<CardViewDataComponent>();
            string cardName = viewPool.Has(victim) ? viewPool.Get(victim).CardName : "";
            UnityEngine.Sprite icon = viewPool.Has(victim) ? viewPool.Get(victim).ArtImage : null;
            var visual = viewPool.Has(victim) ? viewPool.Get(victim).ToVisual() : default;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(victim)) RemoveFromDeckList(world, victim, ownerPool.Get(victim).OwnerId);
            deckTag.Del(victim);

            var graveTag = world.GetPool<GraveTag>();
            if (!graveTag.Has(victim)) graveTag.Add(victim);

            GameEventBus.Publish(new CardMillFromDeckUIEvent
            {
                CardEntity   = victim,
                CardName     = cardName,
                Icon         = icon,
                Visual       = visual,
                IsLocalOwner = world.GetPool<OwnCardTag>().Has(victim),   // своя → крупный показ у CardLayout
            });
        }

        /// <summary>Потратить заряд маркера (обе стороны): Charges−1, ноль → снять. Возвращает вид уплаты.</summary>
        public static AltCostKind ConsumeCharge(EcsWorld world, int playerEntity)
        {
            var pool = world.GetPool<AltCostComponent>();
            ref var alt = ref pool.Get(playerEntity);
            var kind = alt.Kind;
            if (--alt.Charges <= 0) pool.Del(playerEntity);
            return kind;
        }

        // Списки зон (дубль ZoneListUtil — тот internal в сборке Ability).
        static void RemoveFromHandList(EcsWorld world, int card, int ownerId)
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

        static void RemoveFromDeckList(EcsWorld world, int card, int ownerId)
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
    }
}
