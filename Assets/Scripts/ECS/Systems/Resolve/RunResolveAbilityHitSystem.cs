using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunResolveAbilityHitSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<ResolveAbilityEvent, HitViewComponent>> _filter = default;
        readonly EcsPoolInject<HitViewComponent> _hitViewPool = default;
        readonly EcsPoolInject<HitComponent> _hitPool = default;
        readonly EcsPoolInject<ResolveAbilityEvent> _resolveAbilityPool = default;
        readonly EcsPoolInject<TransformComponent> _transformPool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var hitViewComp = ref _hitViewPool.Value.Get(entity);
                ref var resolveAbilityEventComp = ref _resolveAbilityPool.Value.Get(entity);

                _hitPool.Value.Add(resolveAbilityEventComp.ResolveEntity);

                ref var transformComp = ref _transformPool.Value.Add(resolveAbilityEventComp.ResolveEntity);
                transformComp.Transform = GameObject.Instantiate(hitViewComp.Prefab).transform;
            }
        }
    }
}