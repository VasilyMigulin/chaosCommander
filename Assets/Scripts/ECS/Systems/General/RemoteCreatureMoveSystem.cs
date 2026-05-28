using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Принимает RemoteCreatureMoveEvent (пришёл по RPC от оппонента)
    /// и добавляет MoveRequestEvent на entity существа, чтобы MoveSystem
    /// воспроизвёл анимацию и обновил позицию.
    /// </summary>
    public sealed class RemoteCreatureMoveSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject                    _world     = default;
        readonly EcsSharedInject<IGameStateContext> _state     = default;
        readonly EcsPoolInject<MoveRequestEvent>   _movePool  = default;

        readonly List<RemoteCreatureMoveEvent> _pending = new List<RemoteCreatureMoveEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<RemoteCreatureMoveEvent>(OnRemoteMove);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<RemoteCreatureMoveEvent>(OnRemoteMove);
        }

        private void OnRemoteMove(RemoteCreatureMoveEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;

            foreach (var evt in _pending)
                Process(evt);

            _pending.Clear();
        }

        private void Process(RemoteCreatureMoveEvent evt)
        {
            if (!_state.Value.TryGetEntity(evt.CreatureEntityKey, out int entity))
            {
                UnityEngine.Debug.LogWarning($"[RemoteCreatureMoveSystem] Creature not found for key '{evt.CreatureEntityKey}'");
                return;
            }

            if (_movePool.Value.Has(entity)) return;

            ref var req       = ref _movePool.Value.Add(entity);
            req.ToRow         = evt.ToRow;
            req.ToCol         = evt.ToCol;
            req.ToOwnerId     = evt.ToOwnerId;
        }
    }
}
