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

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var queue = ref _queuePool.Value.Get(entity);

                if (!queue.TryPop(out int abilityEntity))
                    continue;

                ref var resolveEvent = ref _resolvePool.Value.Add(abilityEntity);
                resolveEvent.ResolveEntity = _world.Value.NewEntity();

                _lockPool.Value.Add(entity);
            }
        }
    }
}