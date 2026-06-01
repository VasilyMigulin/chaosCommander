using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// В начале хода игрока — декрементит CreatureTimerComponent у существ под его контролем.
    /// При TurnsRemaining ≤ 0 вешает DeadTag (DieSystem отработает штатно).
    /// </summary>
    public sealed class CreatureTimerTickSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<CreatureTimerComponent, BoardTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<CreatureTimerComponent> _timerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
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
            foreach (var entity in _filter.Value)
            {
                if (_ownerPool.Value.Get(entity).OwnerId != playerId) continue;
                ref var t = ref _timerPool.Value.Get(entity);
                t.TurnsRemaining--;
                if (t.TurnsRemaining <= 0 && !_deadPool.Value.Has(entity))
                    _deadPool.Value.Add(entity);
            }
        }
    }
}
