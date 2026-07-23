using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Подписан на CardPlayedEvent: если карта — заклинание (CardType.Spell),
    /// обновляет LastPlayedSpellComponent у entity игрока.
    /// </summary>
    public sealed class LastPlayedSpellTrackerSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<LastPlayedSpellComponent> _lastPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        readonly Queue<CardPlayedEvent> _pending = new Queue<CardPlayedEvent>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnPlayed);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPlayedEvent>(OnPlayed);
        }

        void OnPlayed(CardPlayedEvent evt) => _pending.Enqueue(evt);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Track(_pending.Dequeue());
        }

        void Track(CardPlayedEvent evt)
        {
            if (evt.CardEntity < 0 || !_modelPool.Value.Has(evt.CardEntity)) return;
            ref var m = ref _modelPool.Value.Get(evt.CardEntity);
            if (m.CardType != EnumService.CardType.Spell) return;

            int playerEntity = FindPlayer(evt.PlayerId);
            if (playerEntity < 0) return;

            if (!_lastPool.Value.Has(playerEntity))
                _lastPool.Value.Add(playerEntity);
            ref var last = ref _lastPool.Value.Get(playerEntity);
            last.ExpansionId = m.ExpansionId;
            last.ModelId = m.ModelId;
            last.HasValue = true;
        }

        int FindPlayer(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
