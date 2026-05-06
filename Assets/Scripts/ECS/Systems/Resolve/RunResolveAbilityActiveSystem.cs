using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using UnityEngine;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunResolveAbilityActiveSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<ResolveAbilityEvent>> _filter = default;
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<ResolveAbilityEvent> _resolveAbilityEventPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            { 
                ref var resolveEventComp = ref _resolveAbilityEventPool.Value.Get(entity); 

                _activePool.Value.Add(resolveEventComp.ResolveEntity);
            }
        }
    }
}