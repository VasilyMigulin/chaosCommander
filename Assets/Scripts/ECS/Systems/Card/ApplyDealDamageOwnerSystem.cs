using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет DealDamageOwnerEffectComponent: наносит урон игроку-владельцу
    /// источника способности (через TakeDamageEvent на entity игрока).
    /// </summary>
    public sealed class ApplyDealDamageOwnerSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, DealDamageOwnerEffectComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<DealDamageOwnerEffectComponent> _dmgPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<TakeDamageEvent> _takeDmgPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int amount = _dmgPool.Value.Get(effectEntity).Amount;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;

                int playerEntity = FindOwnerPlayer(abilityEntity);
                if (playerEntity >= 0 && amount > 0)
                {
                    if (_takeDmgPool.Value.Has(playerEntity))
                    {
                        ref var existing = ref _takeDmgPool.Value.Get(playerEntity);
                        existing.Amount += amount;
                    }
                    else
                    {
                        ref var dmg = ref _takeDmgPool.Value.Add(playerEntity);
                        dmg.Amount = amount;
                        dmg.Attacker = -1;
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int FindOwnerPlayer(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return -1;
            int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
            if (sourceCard < 0 || !_ownerPool.Value.Has(sourceCard)) return -1;
            int ownerId = _ownerPool.Value.Get(sourceCard).OwnerId;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }
    }
}
