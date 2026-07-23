using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Принимает UI/дев-запрос завершить ход (RequestEndTurnUIEvent) и навешивает EndTurnRequestEvent
    /// на ЛОКАЛЬНОГО активного игрока (дальше — EndTurnRequestSystem). Это ручной аналог таймера хода.
    /// </summary>
    public sealed class RunRequestEndTurnSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsPoolInject<EndTurnRequestEvent> _reqPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<ActiveState, PlayerComponent, LocalComponent>> _localActiveFilter = default;

        bool _requested;

        public void Init(IEcsSystems systems) => GameEventBus.Subscribe<RequestEndTurnUIEvent>(OnRequest);

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();
        public void Dispose() => GameEventBus.Unsubscribe<RequestEndTurnUIEvent>(OnRequest);
        void OnRequest(RequestEndTurnUIEvent _) => _requested = true;

        public void Run(IEcsSystems systems)
        {
            if (!_requested) return;
            _requested = false;

            foreach (var entity in _localActiveFilter.Value)
            {
                if (!_playerPool.Value.Get(entity).IsLocalPlayer) continue;
                if (!_reqPool.Value.Has(entity))
                {
                    ref var req = ref _reqPool.Value.Add(entity);
                    req.RequestingPlayerId = _playerPool.Value.Get(entity).PlayerId;
                }
                UnityEngine.Debug.Log("[EndTurn] manual end-turn requested");
                break;
            }
        }
    }
}
