using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunResolveAbilityTargetSystem : IEcsRunSystem 
    { 
        readonly EcsFilterInject<Inc<ResolveAbilityEvent, TargetTag, ProjectileViewComponent>> _filter = default;
        readonly EcsPoolInject<ProjectileViewComponent> _projectileViewPool = default;
        readonly EcsPoolInject<ProjectileComponent> _projectilePool = default;
        readonly EcsPoolInject<TransformComponent> _transformPool = default;
        readonly EcsPoolInject<ResolveAbilityEvent> _resolveAbilityEventPool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var projectileViewComp = ref _projectileViewPool.Value.Get(entity);
                ref var resolveEventComp = ref _resolveAbilityEventPool.Value.Get(entity);

                ref var transformComp = ref _transformPool.Value.Add(resolveEventComp.ResolveEntity);
                transformComp.Transform = GameObject.Instantiate(projectileViewComp.Prefab).transform;

                _projectilePool.Value.Add(resolveEventComp.ResolveEntity);
            }
        }
    }
}