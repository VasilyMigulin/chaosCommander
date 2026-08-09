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
    /// Замена механики добора начала хода (Адовый червь): «посмотри N верхних, выбери одну» вместо
    /// взятия верхней карты. Предложение показывается через CardPickOfferedEvent (UI — существующий
    /// PickupWindow); по CardPickChosenEvent выбранная уничтожается (→ кладбище), остальные идут
    /// в руку (DestroyChosen=true).
    ///
    /// Запускается по DrawReplacementDueComponent, который ставит RunTurnStartSystem ВМЕСТО обычного
    /// DrawCardEvent. Сам DrawCardEvent эта система не трогает вовсе — то есть доборы от эффектов карт
    /// идут штатно и замену не запускают, как и написано на карте («когда вы берёте карту в начале хода»).
    /// Перехват DrawCardEvent тут был бы неверен принципиально: DrawCardEffect суммирует Count в уже
    /// существующее событие, и базовый добор от эффектных в нём неотличим.
    ///
    /// Окно выбора — ОБЩИЙ ресурс (его делят раскопка, таргетинг из зон и этот канал), поэтому оффер
    /// публикуется только со слотом от CardPickBrokerSystem, а выбор коррелируется по RequestId.
    /// Прежняя корреляция по CastingCardEntity=entity ИГРОКА была несовместима с остальными каналами
    /// (там это entity КАРТЫ, id из одного пространства, шина общая): чужой выбор не находил pending,
    /// pending висел вечно, и, поскольку DrawCardEvent удаляется здесь безусловно, игрок переставал
    /// добирать карты до конца матча.
    ///
    /// Пик ОБЯЗАТЕЛЬНЫЙ (AllowCancel=false) — иначе отмена приводила бы к тому же зависанию.
    ///
    /// ФАЗА 1 — локально (актив). СИНК — DrawReplacementResolvedNetEvent → ActionDrawReplacementData.
    /// </summary>
    public sealed class RunDrawReplacementSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;   // HandSpace: лимит руки при выдаче выбранной карты
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsPoolInject<DrawReplacementDueComponent>     _duePool     = default;
        readonly EcsPoolInject<DrawReplacementComponent>        _replPool    = default;
        readonly EcsPoolInject<PendingDrawReplacementComponent> _pendingPool = default;
        readonly EcsPoolInject<DeckComponent>                   _deckPool    = default;
        readonly EcsPoolInject<HandComponent>                   _handPool    = default;
        readonly EcsPoolInject<DeckTag>                         _deckTagPool = default;
        readonly EcsPoolInject<HandTag>                         _handTagPool = default;
        readonly EcsPoolInject<GraveTag>                        _graveTagPool = default;
        readonly EcsPoolInject<CardModelComponent>             _modelPool   = default;
        readonly EcsPoolInject<PlayerComponent>                _playerPool  = default;

        readonly EcsFilterInject<Inc<DrawReplacementDueComponent, DrawReplacementComponent, DeckComponent, HandComponent>> _dueFilter = default;
        readonly EcsFilterInject<Inc<PendingDrawReplacementComponent>> _pendingFilter = default;

        bool _subscribed;
        readonly Queue<CardPickChosenEvent>    _chosen    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelled = new Queue<CardPickCancelledEvent>();
        readonly Queue<int>                    _expired   = new Queue<int>();
        readonly List<int>                     _buffer    = new List<int>();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(e => _chosen.Enqueue(e));
            GameEventBus.Subscribe<CardPickCancelledEvent>(e => _cancelled.Enqueue(e));
            // Момент истечения задаёт брокер (одно правило на все каналы пика); способ наш — выбрать
            // случайную из предложенных, чтобы добор не пропал совсем.
            GameEventBus.Subscribe<CardPickExpiredEvent>(e => _expired.Enqueue(e.PlayerId));
        }

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            // Начался ход игрока с заменой: фиксируем предложение (верх колоды на этот момент).
            _buffer.Clear();
            foreach (var pe in _dueFilter.Value) _buffer.Add(pe);
            foreach (var pe in _buffer)
            {
                if (!_pendingPool.Value.Has(pe)) Claim(pe);   // уже ждём выбор — новое предложение не копим
                _duePool.Value.Del(pe);
            }

            // Показ окна отделён от фиксации предложения: слот у брокера может прийти не в этом кадре,
            // а маркер замены живёт ровно один — ждать слот прямо здесь означало бы потерять замену.
            Present();

            while (_chosen.Count > 0)    ResolveChosen(_chosen.Dequeue());
            while (_cancelled.Count > 0) ResolveCancelled(_cancelled.Dequeue());
            while (_expired.Count > 0)   Expire(_expired.Dequeue());
        }

        // Зафиксировать, ЧТО будет предложено (верх колоды на момент добора), не показывая окна.
        void Claim(int playerEntity)
        {
            ref var repl = ref _replPool.Value.Get(playerEntity);
            ref var deck = ref _deckPool.Value.Get(playerEntity);
            if (deck.CardEntities == null || deck.Count == 0) return;   // нечего смотреть

            int n = System.Math.Min(repl.LookCount, deck.Count);
            var offered = new int[n];
            for (int i = 0; i < n; i++) offered[i] = deck.CardEntities[i];

            ref var pending = ref _pendingPool.Value.Add(playerEntity);
            pending.Offered   = offered;
            pending.RequestId = 0;
            pending.Presented = false;
        }

        // Показать окно тем, кому брокер выдал слот.
        void Present()
        {
            _buffer.Clear();
            foreach (var pe in _pendingFilter.Value) _buffer.Add(pe);

            foreach (var pe in _buffer)
            {
                if (!_pendingPool.Value.Has(pe)) continue;
                ref var p = ref _pendingPool.Value.Get(pe);
                if (p.Presented) continue;
                if (p.Offered == null || p.Offered.Length == 0) { ClearPending(pe); continue; }
                if (!PickTicket.Ready(_world.Value, pe, ref p.RequestId, pe)) continue;

                p.Presented = true;
                GameEventBus.Publish(new CardPickOfferedEvent
                {
                    RequestId           = p.RequestId,
                    CastingCardEntity   = pe,             // диагностика; корреляция — только по RequestId
                    PlayerEntity        = pe,
                    OfferedCardEntities = p.Offered,
                    OfferedCardVisuals  = BuildVisuals(p.Offered),
                    OfferedCount        = p.Offered.Length,
                    AllowCancel         = false,          // выбрать одну из трёх ОБЯЗАН
                });
            }
        }

        void ResolveChosen(CardPickChosenEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var pe in _pendingFilter.Value)
            {
                if (_pendingPool.Value.Get(pe).RequestId != e.RequestId) continue;
                Apply(pe, e.ChosenCardEntity);
                return;
            }
        }

        // Пик обязательный, кнопка отмены в окне скрыта (AllowCancel=false). Но отмена может прийти
        // и не из окна, поэтому обрабатываем защитно: сворачиваем случайным выбором, а не «забываем»
        // запрос — иначе он заглушил бы добор до конца матча.
        void ResolveCancelled(CardPickCancelledEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var pe in _pendingFilter.Value)
            {
                if (_pendingPool.Value.Get(pe).RequestId != e.RequestId) continue;
                ForceRandom(pe);
                return;
            }
        }

        // Ход закончился с открытым окном — добор не должен пропасть: роллим сами.
        void Expire(int playerId)
        {
            _buffer.Clear();
            foreach (var pe in _pendingFilter.Value)
                if (PlayerIdOf(pe) == playerId) _buffer.Add(pe);

            foreach (var pe in _buffer)
                if (_pendingPool.Value.Has(pe)) ForceRandom(pe);
        }

        void ForceRandom(int playerEntity)
        {
            var offered = _pendingPool.Value.Get(playerEntity).Offered;
            if (offered == null || offered.Length == 0) { ClearPending(playerEntity); return; }
            Apply(playerEntity, offered[UnityEngine.Random.Range(0, offered.Length)]);
        }

        // ── Терминал: разложить предложенные по зонам и синкнуть результат ───────
        void Apply(int playerEntity, int chosen)
        {
            if (!_pendingPool.Value.Has(playerEntity)) return;

            var offered = _pendingPool.Value.Get(playerEntity).Offered;
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
                    else if (!HandSpace.HasRoom(_world.Value, playerEntity))
                    {
                        // Лимит руки (единое правило, HandSpace): места нет → карта сгорает, как при
                        // обычном доборе в полную руку (Адовый червь не может переполнить руку).
                        HandSpace.Burn(_world.Value, card, "замена добора (Адовый червь)");
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

            ClearPending(playerEntity);

            // СИНК: сообщаем коллектору — он пошлёт ActionDrawReplacementData, пассив повторит у оппонента.
            GameEventBus.Publish(new DrawReplacementResolvedNetEvent
            {
                PlayerEntity  = playerEntity,
                Offered       = offered,
                Chosen        = chosen,
                DestroyChosen = destroyChosen,
            });
        }

        // Сущность ИГРОКА живёт весь матч, поэтому талон окна снимаем явно — сам он не исчезнет.
        void ClearPending(int playerEntity)
        {
            if (_pendingPool.Value.Has(playerEntity)) _pendingPool.Value.Del(playerEntity);
            PickTicket.Release(_world.Value, playerEntity);
        }

        int PlayerIdOf(int playerEntity)
            => _playerPool.Value.Has(playerEntity) ? _playerPool.Value.Get(playerEntity).PlayerId : -1;

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
