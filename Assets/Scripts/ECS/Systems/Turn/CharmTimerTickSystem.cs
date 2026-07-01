using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// В КОНЦЕ хода владельца декрементит CharmTimerComponent у его чар на поле. При TurnsRemaining ≤ 0
    /// вешает DeadTag (CharmDieSystem уничтожит чару). Аналог CreatureTimerTickSystem, но: чары (не существа)
    /// и тик в КОНЦЕ хода (по требованию — «уничтожается после окончания хода»). Тикает только активный
    /// клиент (TurnEndedEvent у него); пассив получит смерть снапшотом (TimerDeathNetEvent → ActionDeathData).
    /// </summary>
    public sealed class CharmTimerTickSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<CharmTimerComponent, BoardTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<CharmTimerComponent> _timerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
            _subscribed = false;
        }
        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
        }
        void OnTurnEnded(TurnEndedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Tick(_pending.Dequeue());
        }

        void Tick(int playerId)
        {
            foreach (var entity in _filter.Value)
            {
                if (_ownerPool.Value.Get(entity).OwnerId != playerId) continue;
                ref var t = ref _timerPool.Value.Get(entity);
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
