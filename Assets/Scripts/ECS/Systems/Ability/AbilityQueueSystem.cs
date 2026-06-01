using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class AbilityQueueSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>, Exc<LockState>> _filter = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsPoolInject<LockState> _lockPool = default;
        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<ReadyTag> _readyPool = default;
        readonly EcsPoolInject<ConditionNotMetTag> _conditionNotMetPool = default;
        readonly EcsPoolInject<ResolveActiveTag> _resolveActivePool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;

        public void Run (IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var queue = ref _queuePool.Value.Get(entity);

                if (!queue.TryPop(out int abilityEntity))
                    continue;

                // Snapshot-репликация: вражеские способности локально НЕ резолвим —
                // они применятся через AbilityResolvedNetEvent / ReplayAbility.
                // Дропаем без лока, чтобы очередь продолжила тикать.
                ref var src = ref _abilitySourcePool.Value.Get(abilityEntity);
                if (!_ownCardPool.Value.Has(src.CardEntity))
                    continue;

                int resolveEntity = _world.Value.NewEntity();
                _resolveActivePool.Value.Add(resolveEntity);
                ref var resolveEvent = ref _resolvePool.Value.Add(abilityEntity);
                resolveEvent.ResolveEntity = resolveEntity;
                resolveEvent.OwnerEntity = src.CardEntity;

                // Если condition способности не выполнен — помечаем resolve как «без эффектов»
                if (!_readyPool.Value.Has(abilityEntity))
                    _conditionNotMetPool.Value.Add(abilityEntity);

                _lockPool.Value.Add(entity);
            }
        }
    }
}