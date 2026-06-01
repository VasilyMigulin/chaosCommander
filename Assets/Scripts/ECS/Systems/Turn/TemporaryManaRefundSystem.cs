using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// В конце хода игрока возвращает временную ману: списывает RefundAmount
    /// и удаляет TemporaryManaComponent.
    /// </summary>
    public sealed class TemporaryManaRefundSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<TemporaryManaComponent, PlayerComponent>> _filter = default;
        readonly EcsPoolInject<TemporaryManaComponent> _tempPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

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
            while (_pending.Count > 0) Refund(_pending.Dequeue());
        }

        void Refund(int playerId)
        {
            foreach (var pe in _filter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != playerId) continue;
                ref var t = ref _tempPool.Value.Get(pe);
                if (t.RefundAmount > 0 && _manaPool.Value.Has(pe))
                {
                    ref var mana = ref _manaPool.Value.Get(pe);
                    mana.Current = System.Math.Max(0, mana.Current - t.RefundAmount);
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = _localPool.Value.Has(pe),
                        Type = EnumService.ResourceType.Mana,
                        NewValue = mana.Current,
                        MaxValue = mana.Max,
                    });
                }
                _tempPool.Value.Del(pe);
            }
        }
    }
}
