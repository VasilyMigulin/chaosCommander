using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Перехватывает CreatureMovedEvent локального игрока и отправляет RPC оппоненту,
    /// чтобы тот воспроизвёл ход существа у себя.
    /// </summary>
    public sealed class NetworkCreatureMoveSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsCustomInject<PhotonRunHandler>      _photon     = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool  = default;

        readonly List<CreatureMovedEvent> _pending = new List<CreatureMovedEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CreatureMovedEvent>(OnCreatureMoved);
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CreatureMovedEvent>(OnCreatureMoved);
        }

        private void OnCreatureMoved(CreatureMovedEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;
            if (_photon.Value == null) { _pending.Clear(); return; }

            foreach (var evt in _pending)
                Sync(evt);

            _pending.Clear();
        }

        private void Sync(CreatureMovedEvent evt)
        {
            int entity = evt.CreatureEntity;
            if (!_netKeyPool.Value.Has(entity)) return;

            string key = _netKeyPool.Value.Get(entity).NetworkEntityKey;
            _photon.Value.RPC_NotifyCreatureMove(key, evt.ToRow, evt.ToCol, evt.ToOwnerId);
        }
    }
}
