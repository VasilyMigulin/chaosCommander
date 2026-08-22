using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// В начале хода игрока — декрементит HandDiscardTimerComponent у карт руки под его владением.
    /// При TurnsRemaining ≤ 0 сбрасывает карту (AltCostUtil.Discard — тот же путь, что у AltCost
    /// DiscardHand). Фильтр требует HandTag: карту, разыгранную/сброшенную раньше срока, таймер уже
    /// не видит — досрочный уход из руки молча гасит его, ничего досбрасывать не нужно.
    /// Тикает только активный клиент (TurnStarted у него); пассив получит сброс снапшотом
    /// (TimerDiscardNetEvent → ActionDiscardData), как и системный death-таймер существ.
    /// </summary>
    public sealed class HandDiscardTimerTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HandDiscardTimerComponent, HandTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<HandDiscardTimerComponent> _timerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }
        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }
        void OnTurnStarted(TurnStartedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Tick(_pending.Dequeue());
        }

        void Tick(int playerId)
        {
            // Снапшот сущностей: AltCostUtil.Discard меняет состав руки (список), фильтр ecslite
            // при удалении из пулов может свопнуть — итерируем по зафиксированному списку.
            var batch = new List<int>();
            foreach (var entity in _filter.Value) batch.Add(entity);

            foreach (var entity in batch)
            {
                if (!_timerPool.Value.Has(entity)) continue;   // уже сброшена этим же тиком (не должно, но не полагаемся)
                if (_ownerPool.Value.Get(entity).OwnerId != playerId) continue;
                ref var t = ref _timerPool.Value.Get(entity);
                t.TurnsRemaining--;
                if (t.TurnsRemaining <= 0)
                {
                    _timerPool.Value.Del(entity);
                    AltCostUtil.Discard(_world.Value, entity);
                    // Синк: сброс от таймера у пассива сам не наступит (он не тикает чужой таймер
                    // в чужой ход) → сообщаем коллектору, он пошлёт ActionDiscardData.
                    GameEventBus.Publish(new TimerDiscardNetEvent { CardEntity = entity });
                }
            }
        }
    }
}
