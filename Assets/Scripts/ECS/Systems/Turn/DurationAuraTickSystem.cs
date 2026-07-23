using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// На каждый TurnStartedEvent для активного игрока — уменьшает TurnsRemaining
    /// у чар-источников с DurationAuraComponent, принадлежащих этому игроку.
    /// При TurnsRemaining ≤ 0: снимает BoardTag, вешает GraveTag, удаляет визуал —
    /// AuraRecalcSystem перестанет применять её бонусы со следующего кадра.
    /// </summary>
    public sealed class DurationAuraTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<DurationAuraComponent, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<DurationAuraComponent> _durationPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<GraveTag> _gravePool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;

        readonly Queue<int> _pendingTicks = new Queue<int>();
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

        void OnTurnStarted(TurnStartedEvent evt) => _pendingTicks.Enqueue(evt.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pendingTicks.Count > 0)
                Tick(_pendingTicks.Dequeue());
        }

        void Tick(int activePlayerId)
        {
            foreach (var charmEntity in _filter.Value)
            {
                if (_ownerPool.Value.Get(charmEntity).OwnerId != activePlayerId) continue;
                if (!_boardPool.Value.Has(charmEntity)) continue; // ауру уже не применяем

                ref var dur = ref _durationPool.Value.Get(charmEntity);
                dur.TurnsRemaining--;
                if (dur.TurnsRemaining > 0) continue;

                // Срок истёк — снимаем чару с поля.
                _durationPool.Value.Del(charmEntity);
                _boardPool.Value.Del(charmEntity);
                if (!_gravePool.Value.Has(charmEntity))
                    _gravePool.Value.Add(charmEntity);

                if (_viewPool.Value.Has(charmEntity))
                {
                    ref var vr = ref _viewPool.Value.Get(charmEntity);
                    if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
                    vr.View = null;
                }
                if (_spawnedPool.Value.Has(charmEntity))
                    _spawnedPool.Value.Del(charmEntity);
            }
        }
    }
}
