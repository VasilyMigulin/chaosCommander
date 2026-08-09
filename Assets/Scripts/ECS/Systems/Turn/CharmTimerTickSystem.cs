using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

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
        readonly EcsFilterInject<Inc<CharmTimerComponent, BoardTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<CharmTimerComponent> _timerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        readonly Queue<(int playerId, CharmTickMoment moment)> _pending = new();
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
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        void OnTurnEnded(TurnEndedEvent e)     => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnEnd));
        void OnTurnStarted(TurnStartedEvent e) => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnStart));

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var (playerId, moment) = _pending.Dequeue();
                Tick(playerId, moment);
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
            }
        }
    }
}
