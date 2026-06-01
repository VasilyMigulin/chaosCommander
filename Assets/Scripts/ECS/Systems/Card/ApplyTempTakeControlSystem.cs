using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет TempTakeControlEffectComponent: сохраняет оригинальные владения
    /// в TempControlledComponent, переключает на владельца источника.
    /// TempControlRevertSystem откатит на TurnEnd владельца источника.
    /// </summary>
    public sealed class ApplyTempTakeControlSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, TempTakeControlEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyTagPool = default;
        readonly EcsPoolInject<TempControlledComponent> _tempPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;

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
                        int newSide = _sidePool.Value.Has(sourceCard) ? _sidePool.Value.Get(sourceCard).Side : -1;

                        // Сохраняем оригиналы
                        if (!_tempPool.Value.Has(target))
                        {
                            ref var t = ref _tempPool.Value.Add(target);
                            t.OriginalOwnerId = _ownerPool.Value.Has(target) ? _ownerPool.Value.Get(target).OwnerId : -1;
                            t.OriginalSide   = _boardPosPool.Value.Has(target) ? _boardPosPool.Value.Get(target).OwnerId : -1;
                            t.OriginalWasOwn = _ownTagPool.Value.Has(target);
                            t.ExpiresOnPlayerId = newOwnerId;
                        }

                        // Переключаем владение
                        if (_ownerPool.Value.Has(target)) _ownerPool.Value.Get(target).OwnerId = newOwnerId;
                        if (_boardPosPool.Value.Has(target) && newSide >= 0)
                            _boardPosPool.Value.Get(target).OwnerId = newSide;
                        if (_sidePool.Value.Has(target) && newSide >= 0)
                            _sidePool.Value.Get(target).Side = newSide;

                        if (sourceIsOwn)
                        {
                            if (_enemyTagPool.Value.Has(target)) _enemyTagPool.Value.Del(target);
                            if (!_ownTagPool.Value.Has(target))  _ownTagPool.Value.Add(target);
                        }
                        else
                        {
                            if (_ownTagPool.Value.Has(target))   _ownTagPool.Value.Del(target);
                            if (!_enemyTagPool.Value.Has(target)) _enemyTagPool.Value.Add(target);
                        }

                        if (_viewPool.Value.Has(target))
                        {
                            ref var vr = ref _viewPool.Value.Get(target);
                            if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
                            vr.View = null;
                        }
                        if (_spawnedPool.Value.Has(target)) _spawnedPool.Value.Del(target);
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
