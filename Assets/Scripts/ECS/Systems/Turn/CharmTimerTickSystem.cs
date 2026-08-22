using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Model.Card;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Декрементит CharmTimerComponent у чар владельца на поле; при TurnsRemaining ≤ 0 вешает DeadTag
    /// (CharmDieSystem уничтожит чару). МОМЕНТ тика задаёт сама чара (CardCharmModel.TickMoment →
    /// CharmTimerComponent.Moment, решение юзера 2026-07-30):
    ///   • TurnEnd (умолчание, прежнее поведение) — в конце хода владельца: «1» = до конца этого хода,
    ///     «2» = переживёт ход оппонента. Годится для постоянных эффектов и «в конце хода».
    ///   • TurnStart — в начале хода владельца: «1» = сработает ровно один раз в следующий свой ход.
    ///     Чинит класс проблем, когда чара с эффектом «в начале хода» умирала в конце хода розыгрыша,
    ///     не отработав НИ РАЗУ.
    /// ПОРЯДОК при TurnStart безопасен: триггеры помечаются СИНХРОННО на публикации TurnStartedEvent
    /// (AbilityFire.Mark), а тик кладётся в очередь и выполняется в Run — то есть эффект чары уже
    /// поставлен в пайплайн и отрезолвится, даже если чара этим же тиком умирает.
    /// Тикает только активный клиент (TurnStarted/TurnEnded у него); пассив получит смерть снапшотом
    /// (TimerDeathNetEvent → ActionDeathData).
    /// </summary>
    public sealed class CharmTimerTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;
        readonly EcsFilterInject<Inc<CharmTimerComponent, BoardTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<CharmTimerComponent> _timerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        readonly Queue<(int playerId, CharmTickMoment moment)> _pending = new();
        readonly Queue<int> _pendingBumped = new();   // CharmTimerBumpedEvent — просят только перерендер, не тик
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Unsubscribe<CharmTimerBumpedEvent>(OnCharmTimerBumped);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Subscribe<CharmTimerBumpedEvent>(OnCharmTimerBumped);
        }

        void OnTurnEnded(TurnEndedEvent e)     => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnEnd));
        void OnTurnStarted(TurnStartedEvent e) => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnStart));
        void OnCharmTimerBumped(CharmTimerBumpedEvent e) => _pendingBumped.Enqueue(e.CardEntity);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var (playerId, moment) = _pending.Dequeue();
                Tick(playerId, moment);
            }

            // Ретроактивный бонус (AddCharmDurationBonusEffect) уже поправил TurnsRemaining сам —
            // тут только перерендер бейджа/описания под новое значение, без декремента/death-check.
            while (_pendingBumped.Count > 0)
            {
                int e = _pendingBumped.Dequeue();
                if (_timerPool.Value.Has(e) && !_deadPool.Value.Has(e)) RefreshDescription(e);
            }
        }

        void Tick(int playerId, CharmTickMoment moment)
        {
            foreach (var entity in _filter.Value)
            {
                if (_ownerPool.Value.Get(entity).OwnerId != playerId) continue;
                ref var t = ref _timerPool.Value.Get(entity);
                if (t.Moment != moment) continue;   // чара тикает в СВОЙ момент

                t.TurnsRemaining--;
                if (t.TurnsRemaining <= 0 && !_deadPool.Value.Has(entity))
                {
                    _deadPool.Value.Add(entity);
                    // Синк: пассив сам не тикает чужой таймер → шлём смерть, он повторит (как у существ).
                    GameEventBus.Publish(new TimerDeathNetEvent { CreatureEntity = entity });
                }
                else
                {
                    // Живая чара на столе: снэпшот описания под свежий TurnsRemaining + событие для
                    // открытого инспект-попапа/индикатора — раньше тут просто молчали, и «Действует N ходов»
                    // застывало на печатном значении навсегда, даже без Зачарованного (баг 2026-08-21).
                    RefreshDescription(entity);
                }
            }
        }

        void RefreshDescription(int cardEntity)
        {
            if (!_modelPool.Value.Has(cardEntity)) return;
            ref var m = ref _modelPool.Value.Get(cardEntity);
            var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
            var model = inst?.CardData;
            if (model == null) return;

            string key = CardTextLocalization.DescKey(model.ExpansionId, model.Id);
            int playerEntity = OwnerPlayerEntity(cardEntity);
            var live = CardDynamicValues.Collect(_world.Value, cardEntity, playerEntity);
            string desc = CardDescriptionFormatter.Format(key, model.Description, model.GetCardType(),
                _timerPool.Value.Get(cardEntity).TurnsRemaining, live);

            if (_viewPool.Value.Has(cardEntity)) _viewPool.Value.Get(cardEntity).Description = desc;
            GameEventBus.Publish(new CardDescriptionChangedUIEvent { CardEntity = cardEntity, Description = desc });
        }

        int OwnerPlayerEntity(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return -1;
            int ownerId = _ownerPool.Value.Get(cardEntity).OwnerId;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }
    }
}
