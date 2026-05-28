using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Перехватывает CreatureAttackedEvent локального игрока и отправляет RPC оппоненту.
    /// </summary>
    public sealed class NetworkCreatureAttackSystem : IEcsRunSystem, IEcsInitSystem, System.IDisposable
    {
        readonly EcsCustomInject<PhotonRunHandler>      _photon     = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool  = default;

        readonly List<CreatureAttackedEvent> _pending = new List<CreatureAttackedEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CreatureAttackedEvent>(OnCreatureAttacked);
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CreatureAttackedEvent>(OnCreatureAttacked);
        }

        private void OnCreatureAttacked(CreatureAttackedEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;
            if (_photon.Value == null) { _pending.Clear(); return; }

            foreach (var evt in _pending)
                Sync(evt);

            _pending.Clear();
        }

        private void Sync(CreatureAttackedEvent evt)
        {
            int attacker = evt.AttackerEntity;
            int defender = evt.DefenderEntity;

            if (!_netKeyPool.Value.Has(attacker)) return;
            if (!_netKeyPool.Value.Has(defender)) return;

            // Синхронизируем только атаки своих существ (чтобы не дублировать RPC)
            if (!_ownerPool.Value.Has(attacker)) return;

            string attackerKey = _netKeyPool.Value.Get(attacker).NetworkEntityKey;
            string defenderKey = _netKeyPool.Value.Get(defender).NetworkEntityKey;

            _photon.Value.RPC_NotifyCreatureAttack(attackerKey, defenderKey);
        }
    }
}
