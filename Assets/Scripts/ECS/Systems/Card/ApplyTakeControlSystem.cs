using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет TakeControlEffectComponent: меняет OwnerComponent + BoardPosition.OwnerId
    /// на владельца источника, свопит OwnCardTag/EnemyCardTag и деспавнит визуал —
    /// SpawnCreatureViewSystem создаст новый на нужной стороне.
    /// </summary>
    public sealed class ApplyTakeControlSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, TakeControlEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyTagPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;

                if (target >= 0 && _abilitySourcePool.Value.Has(abilityEntity))
                {
                    int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
                    if (sourceCard >= 0 && _ownerPool.Value.Has(sourceCard))
                    {
                        int newOwnerId = _ownerPool.Value.Get(sourceCard).OwnerId;
                        bool sourceIsOwn = _ownTagPool.Value.Has(sourceCard);
                        int newSide = _sidePool.Value.Has(sourceCard)
                            ? _sidePool.Value.Get(sourceCard).Side : -1;

                        Transfer(target, newOwnerId, sourceIsOwn, newSide);
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        void Transfer(int target, int newOwnerId, bool sourceIsOwn, int newSide)
        {
            if (_ownerPool.Value.Has(target))
            {
                ref var o = ref _ownerPool.Value.Get(target);
                o.OwnerId = newOwnerId;
            }
            if (_boardPosPool.Value.Has(target) && newSide >= 0)
            {
                ref var bp = ref _boardPosPool.Value.Get(target);
                bp.OwnerId = newSide;
            }
            if (_sidePool.Value.Has(target) && newSide >= 0)
            {
                ref var s = ref _sidePool.Value.Get(target);
                s.Side = newSide;
            }

            // Своп тегов owner/enemy относительно ЛОКАЛЬНОГО клиента: источник свой → жертва тоже становится своей.
            if (sourceIsOwn)
            {
                if (_enemyTagPool.Value.Has(target)) _enemyTagPool.Value.Del(target);
                if (!_ownTagPool.Value.Has(target)) _ownTagPool.Value.Add(target);
            }
            else
            {
                if (_ownTagPool.Value.Has(target)) _ownTagPool.Value.Del(target);
                if (!_enemyTagPool.Value.Has(target)) _enemyTagPool.Value.Add(target);
            }

            // Деспавним визуал — SpawnCreatureViewSystem пересоздаст его на новой стороне.
            if (_viewPool.Value.Has(target))
            {
                ref var vr = ref _viewPool.Value.Get(target);
                if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
                vr.View = null;
            }
            if (_spawnedPool.Value.Has(target)) _spawnedPool.Value.Del(target);
        }
    }
}
