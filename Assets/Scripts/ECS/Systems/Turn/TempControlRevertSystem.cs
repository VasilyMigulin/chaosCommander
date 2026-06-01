using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// На TurnEndedEvent владельца возвращает временный контроль:
    /// откатывает OwnerComponent / BoardPosition.OwnerId / PlayerSide и теги.
    /// </summary>
    public sealed class TempControlRevertSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<TempControlledComponent>> _filter = default;
        readonly EcsPoolInject<TempControlledComponent> _tempPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyTagPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
            _subscribed = false;
        }
        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
        }
        void OnTurnEnded(TurnEndedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Revert(_pending.Dequeue());
        }

        void Revert(int playerId)
        {
            foreach (var entity in _filter.Value)
            {
                ref var t = ref _tempPool.Value.Get(entity);
                if (t.ExpiresOnPlayerId != playerId) continue;

                if (_ownerPool.Value.Has(entity) && t.OriginalOwnerId >= 0)
                    _ownerPool.Value.Get(entity).OwnerId = t.OriginalOwnerId;
                if (_boardPosPool.Value.Has(entity) && t.OriginalSide >= 0)
                    _boardPosPool.Value.Get(entity).OwnerId = t.OriginalSide;
                if (_sidePool.Value.Has(entity) && t.OriginalSide >= 0)
                    _sidePool.Value.Get(entity).Side = t.OriginalSide;

                if (t.OriginalWasOwn)
                {
                    if (_enemyTagPool.Value.Has(entity)) _enemyTagPool.Value.Del(entity);
                    if (!_ownTagPool.Value.Has(entity))  _ownTagPool.Value.Add(entity);
                }
                else
                {
                    if (_ownTagPool.Value.Has(entity))   _ownTagPool.Value.Del(entity);
                    if (!_enemyTagPool.Value.Has(entity)) _enemyTagPool.Value.Add(entity);
                }

                if (_viewPool.Value.Has(entity))
                {
                    ref var vr = ref _viewPool.Value.Get(entity);
                    if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
                    vr.View = null;
                }
                if (_spawnedPool.Value.Has(entity)) _spawnedPool.Value.Del(entity);

                _tempPool.Value.Del(entity);
            }
        }
    }
}
