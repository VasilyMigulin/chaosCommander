using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет ReturnToHandEffectComponent: переносит TargetEntity (карту/существо)
    /// в руку её владельца. Снимает BoardTag/GraveTag/BoardPositionComponent, добавляет
    /// HandTag, кладёт в HandComponent. Уничтожает визуал на доске (CreatureView), чтобы
    /// при повторном развёртывании создался новый инстанс.
    /// </summary>
    public sealed class ApplyReturnToHandSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, ReturnToHandEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                if (target >= 0)
                    Return(target);

                _world.Value.DelEntity(effectEntity);
            }
        }

        void Return(int cardEntity)
        {
            if (_boardTagPool.Value.Has(cardEntity))  _boardTagPool.Value.Del(cardEntity);
            if (_graveTagPool.Value.Has(cardEntity))  _graveTagPool.Value.Del(cardEntity);
            if (_boardPosPool.Value.Has(cardEntity))  _boardPosPool.Value.Del(cardEntity);

            // Деспавним визуал, чтобы повторный спавн через SpawnCreatureViewSystem создал чистый инстанс.
            if (_viewPool.Value.Has(cardEntity))
            {
                ref var vr = ref _viewPool.Value.Get(cardEntity);
                if (vr.View != null)
                {
                    UnityEngine.Object.Destroy(vr.View);
                    vr.View = null;
                }
            }
            if (_spawnedPool.Value.Has(cardEntity)) _spawnedPool.Value.Del(cardEntity);

            // Восстанавливаем статы существа к базе (возврат в руку = новый розыгрыш).
            if (_hpPool.Value.Has(cardEntity))
            {
                ref var hp = ref _hpPool.Value.Get(cardEntity);
                hp.Max = hp.BaseMax;
                hp.Current = hp.BaseMax;
            }
            if (_speedPool.Value.Has(cardEntity))
            {
                ref var sp = ref _speedPool.Value.Get(cardEntity);
                sp.Remaining = sp.Max;
            }

            if (!_handTagPool.Value.Has(cardEntity)) _handTagPool.Value.Add(cardEntity);

            int ownerId = _ownerPool.Value.Has(cardEntity) ? _ownerPool.Value.Get(cardEntity).OwnerId : -1;
            int playerEntity = FindPlayer(ownerId);
            if (playerEntity >= 0)
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities == null)
                    hand.CardEntities = new System.Collections.Generic.List<int>();
                if (!hand.CardEntities.Contains(cardEntity))
                {
                    hand.CardEntities.Add(cardEntity);
                    hand.Count = hand.CardEntities.Count;
                }
            }
        }

        int FindPlayer(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
