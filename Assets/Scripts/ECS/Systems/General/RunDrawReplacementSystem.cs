using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Замена добора (Адовый червь). Стоит ПЕРЕД DrawCardSystem: для игрока с DrawReplacementComponent
    /// отменяет обычный добор и предлагает выбор «смотри N верхних» через CardPickOfferedEvent (UI —
    /// существующий PickupWindow). По CardPickChosenEvent выбранную уничтожает (→ кладбище), остальные
    /// берёт в руку (DestroyChosen=true). Выбор коррелируется по сущности ИГРОКА (CastingCardEntity).
    ///
    /// ФАЗА 1 — локально (актив). СИНК (передать offered+chosen пассиву) — отдельным куском.
    /// </summary>
    public sealed class RunDrawReplacementSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsPoolInject<DrawCardEvent>                   _drawPool    = default;
        readonly EcsPoolInject<DrawReplacementComponent>        _replPool    = default;
        readonly EcsPoolInject<PendingDrawReplacementComponent> _pendingPool = default;
        readonly EcsPoolInject<DeckComponent>                   _deckPool    = default;
        readonly EcsPoolInject<HandComponent>                   _handPool    = default;
        readonly EcsPoolInject<DeckTag>                         _deckTagPool = default;
        readonly EcsPoolInject<HandTag>                         _handTagPool = default;
        readonly EcsPoolInject<GraveTag>                        _graveTagPool = default;
        readonly EcsPoolInject<CardModelComponent>             _modelPool   = default;

        readonly EcsFilterInject<Inc<DrawCardEvent, DrawReplacementComponent, DeckComponent, HandComponent>> _drawFilter = default;

        bool _subscribed;
        readonly Queue<CardPickChosenEvent> _chosen = new Queue<CardPickChosenEvent>();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(OnChosen);
        }

        void OnChosen(CardPickChosenEvent e) => _chosen.Enqueue(e);

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            // Перехват добора: отменяем обычный, предлагаем выбор (или ждём текущий).
            var buffer = new List<int>();
            foreach (var pe in _drawFilter.Value) buffer.Add(pe);
            foreach (var pe in buffer)
            {
                if (!_pendingPool.Value.Has(pe)) Offer(pe);   // уже ждём — просто отменяем добор ниже
                _drawPool.Value.Del(pe);                      // обычного добора не будет
            }

            while (_chosen.Count > 0) Resolve(_chosen.Dequeue());
        }

        void Offer(int playerEntity)
        {
            ref var repl = ref _replPool.Value.Get(playerEntity);
            ref var deck = ref _deckPool.Value.Get(playerEntity);
            if (deck.CardEntities == null || deck.Count == 0) return;   // нечего смотреть

            int n = System.Math.Min(repl.LookCount, deck.Count);
            var offered = new int[n];
            for (int i = 0; i < n; i++) offered[i] = deck.CardEntities[i];

            _pendingPool.Value.Add(playerEntity).Offered = offered;

            GameEventBus.Publish(new CardPickOfferedEvent
            {
                CastingCardEntity   = playerEntity,   // корреляция (эхо в CardPickChosenEvent)
                PlayerEntity        = playerEntity,
                OfferedCardEntities = offered,
                OfferedCardVisuals  = BuildVisuals(offered),
                OfferedCount        = n,
            });
        }

        void Resolve(CardPickChosenEvent e)
        {
            int playerEntity = e.CastingCardEntity;
            if (!_pendingPool.Value.Has(playerEntity)) return;   // не наш выбор / устарел

            var offered = _pendingPool.Value.Get(playerEntity).Offered;
            int chosen = e.ChosenCardEntity;
            bool destroyChosen = _replPool.Value.Has(playerEntity) && _replPool.Value.Get(playerEntity).DestroyChosen;

            ref var deck = ref _deckPool.Value.Get(playerEntity);
            ref var hand = ref _handPool.Value.Get(playerEntity);

            if (offered != null)
                foreach (var card in offered)
                {
                    if (deck.CardEntities != null) deck.CardEntities.Remove(card);
                    if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);

                    bool destroy = destroyChosen ? (card == chosen) : (card != chosen);
                    if (destroy)
                    {
                        if (!_graveTagPool.Value.Has(card)) _graveTagPool.Value.Add(card);   // уничтожена → кладбище
                    }
                    else
                    {
                        if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
                        hand.CardEntities.Add(card);
                        GameEventBus.Publish(new CardDrawnEvent { CardEntity = card, PlayerId = playerEntity });
                    }
                }

            deck.Count = deck.CardEntities != null ? deck.CardEntities.Count : 0;
            hand.Count = hand.CardEntities.Count;

            _pendingPool.Value.Del(playerEntity);

            // СИНК: сообщаем коллектору — он пошлёт ActionDrawReplacementData, пассив повторит у оппонента.
            GameEventBus.Publish(new DrawReplacementResolvedNetEvent
            {
                PlayerEntity  = playerEntity,
                Offered       = offered,
                Chosen        = chosen,
                DestroyChosen = destroyChosen,
            });
        }

        CardVisualData[] BuildVisuals(int[] cards)
        {
            var res = new CardVisualData[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                if (!_modelPool.Value.Has(cards[i])) continue;
                ref var m = ref _modelPool.Value.Get(cards[i]);
                var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
                if (inst?.CardData != null) res[i] = CardVisualDataFactory.From(inst.CardData);
            }
            return res;
        }
    }
}
