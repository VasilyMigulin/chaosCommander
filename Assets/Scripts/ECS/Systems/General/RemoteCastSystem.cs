using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Принимает RemoteCardCastEvent (пришёл по RPC от оппонента)
    /// и детерминированно воспроизводит каст: создаёт CastEvent на entity карты
    /// (уже существующей на этом клиенте как EnemyCardTag), подставляя TargetEntity
    /// или TargetCell из пакета.
    ///
    /// Система запускается только один раз за кадр для каждого пришедшего пакета.
    /// </summary>
    public sealed class RemoteCastSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsPoolInject<CastEvent>        _castPool   = default;
        readonly EcsPoolInject<EnemyCardTag>     _enemyPool  = default;
        readonly EcsPoolInject<PlayerComponent>  _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        readonly List<RemoteCardCastEvent> _pending = new List<RemoteCardCastEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<RemoteCardCastEvent>(OnRemoteCast);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<RemoteCardCastEvent>(OnRemoteCast);
        }

        private void OnRemoteCast(RemoteCardCastEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;

            foreach (var evt in _pending)
                Process(evt);

            _pending.Clear();
        }

        private void Process(RemoteCardCastEvent evt)
        {
            // Найти entity карты оппонента по сетевому ключу
            if (!_state.Value.TryGetEntity(evt.CardEntityKey, out int cardEntity))
            {
                UnityEngine.Debug.LogWarning($"[RemoteCastSystem] Card entity not found for key '{evt.CardEntityKey}'");
                return;
            }

            if (!_enemyPool.Value.Has(cardEntity))
            {
                UnityEngine.Debug.LogWarning($"[RemoteCastSystem] Entity {cardEntity} is not an enemy card, skipping.");
                return;
            }

            // Уже в каст-очереди — дубликат
            if (_castPool.Value.Has(cardEntity)) return;

            // Найти entity игрока-владельца карты
            int ownerEntity = FindOwnerOf(cardEntity);

            ref var cast = ref _castPool.Value.Add(cardEntity);
            cast.OwnerEntity  = ownerEntity;
            cast.TargetCell   = evt.TargetCell;
            cast.TargetEntity = -1;

            // Если есть ключ цели — ищем entity
            if (!string.IsNullOrEmpty(evt.TargetEntityKey))
            {
                if (_state.Value.TryGetEntity(evt.TargetEntityKey, out int targetEntity))
                    cast.TargetEntity = targetEntity;
            }
        }

        private int FindOwnerOf(int cardEntity)
        {
            var ownerPool = _world.Value.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return -1;

            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            foreach (var pe in _playerFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(pe);
                if (player.PlayerId == ownerId) return pe;
            }
            return -1;
        }
    }
}
