using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет CastTargetCardEffectComponent: вешает CastEvent на TargetEntity
    /// (обычно — карту, выбранную раскопкой). Владелец каста определяется по
    /// OwnerComponent.OwnerId карты-источника цепочки (entity игрока ищется через
    /// PlayerComponent.PlayerId).
    /// </summary>
    public sealed class ApplyCastTargetCardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, CastTargetCardEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int targetCard = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;

                if (targetCard >= 0 && !_castPool.Value.Has(targetCard))
                {
                    int playerEntity = FindPlayerForAbility(abilityEntity);
                    if (playerEntity >= 0)
                    {
                        ref var cast = ref _castPool.Value.Add(targetCard);
                        cast.OwnerEntity  = playerEntity;
                        cast.TargetEntity = -1;
                        cast.TargetCell   = -1;
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int FindPlayerForAbility(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return -1;
            int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
            if (sourceCard < 0 || !_ownerPool.Value.Has(sourceCard)) return -1;
            int ownerId = _ownerPool.Value.Get(sourceCard).OwnerId;

            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId == ownerId)
                    return pe;
            }
            return -1;
        }
    }
}
