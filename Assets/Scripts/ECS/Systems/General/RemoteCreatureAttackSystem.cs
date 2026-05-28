using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Принимает RemoteCreatureAttackEvent (пришёл по RPC от оппонента)
    /// и добавляет AttackRequestEvent на entity атакующего, чтобы AttackSystem
    /// воспроизвёл анимацию и применил урон.
    /// </summary>
    public sealed class RemoteCreatureAttackSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject                     _world      = default;
        readonly EcsSharedInject<IGameStateContext>  _state      = default;
        readonly EcsPoolInject<AttackRequestEvent>  _attackPool = default;

        readonly List<RemoteCreatureAttackEvent> _pending = new List<RemoteCreatureAttackEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<RemoteCreatureAttackEvent>(OnRemoteAttack);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<RemoteCreatureAttackEvent>(OnRemoteAttack);
        }

        private void OnRemoteAttack(RemoteCreatureAttackEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;

            foreach (var evt in _pending)
                Process(evt);

            _pending.Clear();
        }

        private void Process(RemoteCreatureAttackEvent evt)
        {
            if (!_state.Value.TryGetEntity(evt.AttackerEntityKey, out int attacker))
            {
                UnityEngine.Debug.LogWarning($"[RemoteCreatureAttackSystem] Attacker not found for key '{evt.AttackerEntityKey}'");
                return;
            }

            if (!_state.Value.TryGetEntity(evt.DefenderEntityKey, out int defender))
            {
                UnityEngine.Debug.LogWarning($"[RemoteCreatureAttackSystem] Defender not found for key '{evt.DefenderEntityKey}'");
                return;
            }

            if (_attackPool.Value.Has(attacker)) return;

            ref var req        = ref _attackPool.Value.Add(attacker);
            req.TargetEntity   = defender;
        }
    }
}
