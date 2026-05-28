using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// После того как CastCardSystem публикует CardPlayedEvent для локальной карты,
    /// этот система отправляет RPC оппоненту с ключами карты и цели.
    ///
    /// Оппонент получит RemoteCardCastEvent → RemoteCastSystem создаст CastEvent на
    /// карте оппонента детерминированно.
    /// </summary>
    public sealed class NetworkCardCastSystem : IEcsRunSystem, IEcsInitSystem, System.IDisposable
    {
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool    = default;
        readonly EcsPoolInject<OwnCardTag>             _ownCardPool   = default;

        readonly List<CardPlayedEvent> _pending = new List<CardPlayedEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardPlayedEvent>(OnCardPlayed);
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnCardPlayed);
        }

        private void OnCardPlayed(CardPlayedEvent evt)
        {
            _pending.Add(evt);
        }

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;
            if (_photon.Value == null) { _pending.Clear(); return; }

            foreach (var evt in _pending)
                Sync(evt);

            _pending.Clear();
        }

        private void Sync(CardPlayedEvent evt)
        {
            int cardEntity = evt.CardEntity;

            // Синхронизируем только свои карты
            if (!_ownCardPool.Value.Has(cardEntity)) return;
            if (!_netKeyPool.Value.Has(cardEntity)) return;

            string cardKey = _netKeyPool.Value.Get(cardEntity).NetworkEntityKey;

            string targetKey = string.Empty;
            if (evt.TargetEntity >= 0 && _netKeyPool.Value.Has(evt.TargetEntity))
                targetKey = _netKeyPool.Value.Get(evt.TargetEntity).NetworkEntityKey;

            _photon.Value.RPC_NotifyCardCast(cardKey, targetKey, evt.TargetCell);
        }
    }
}
