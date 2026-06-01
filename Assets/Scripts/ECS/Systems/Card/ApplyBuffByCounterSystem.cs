using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет BuffByCounterEffectComponent: умножает PerCount на счётчик
    /// MatchCounterComponent у владельца источника и пишет в Base/BaseMax цели
    /// (постоянный бафф — попадает в AuraRecalcSystem).
    /// </summary>
    public sealed class ApplyBuffByCounterSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, BuffByCounterEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<BuffByCounterEffectComponent> _buffPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<MatchCounterComponent> _counterPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _buffPool.Value.Get(effectEntity);
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;

                int count = GetCountForAbilityOwner(abilityEntity, data.CounterModelId);
                if (count > 0 && target >= 0)
                {
                    int atkBonus = data.AttackPerCount * count;
                    int hpBonus  = data.HealthPerCount * count;

                    if (atkBonus != 0 && _attackPool.Value.Has(target))
                    {
                        ref var a = ref _attackPool.Value.Get(target);
                        a.Base += atkBonus;
                        a.Value += atkBonus;
                    }
                    if (hpBonus != 0 && _hpPool.Value.Has(target))
                    {
                        ref var h = ref _hpPool.Value.Get(target);
                        h.BaseMax += hpBonus;
                        h.Max += hpBonus;
                        h.Current += hpBonus;
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int GetCountForAbilityOwner(int abilityEntity, int modelId)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return 0;
            int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
            if (sourceCard < 0 || !_ownerPool.Value.Has(sourceCard)) return 0;
            int ownerId = _ownerPool.Value.Get(sourceCard).OwnerId;

            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != ownerId) continue;
                if (!_counterPool.Value.Has(pe)) return 0;
                ref var c = ref _counterPool.Value.Get(pe);
                if (c.CountsByModelId == null) return 0;
                c.CountsByModelId.TryGetValue(modelId, out int cnt);
                return cnt;
            }
            return 0;
        }
    }
}
