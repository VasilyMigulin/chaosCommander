using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Подписан на CardPlayedEvent: инкрементит MatchCounterComponent (ModelId → count)
    /// у entity игрока, разыгравшего карту. Если у игрока нет MatchCounterComponent — создаёт.
    /// Снапшоты не нужны — обе стороны видят CardPlayedEvent (он публикуется и при реплее каста).
    /// </summary>
    public sealed class MatchCounterTrackerSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<MatchCounterComponent> _counterPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        readonly Queue<CardPlayedEvent> _pending = new Queue<CardPlayedEvent>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnCardPlayed);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPlayedEvent>(OnCardPlayed);
        }

        void OnCardPlayed(CardPlayedEvent evt) => _pending.Enqueue(evt);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
                Track(_pending.Dequeue());
        }

        void Track(CardPlayedEvent evt)
        {
            if (evt.CardEntity < 0) return;
            if (!_modelPool.Value.Has(evt.CardEntity)) return;
            int modelId = _modelPool.Value.Get(evt.CardEntity).ModelId;

            int playerEntity = FindPlayerEntity(evt.PlayerId);
            if (playerEntity < 0) return;

            if (!_counterPool.Value.Has(playerEntity))
            {
                ref var c = ref _counterPool.Value.Add(playerEntity);
                c.CountsByModelId = new Dictionary<int, int>();
            }
            ref var counter = ref _counterPool.Value.Get(playerEntity);
            counter.CountsByModelId.TryGetValue(modelId, out int cur);
            counter.CountsByModelId[modelId] = cur + 1;
        }

        int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
