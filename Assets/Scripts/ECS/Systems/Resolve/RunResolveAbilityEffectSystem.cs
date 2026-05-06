using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunResolveAbilityEffectSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<ResolveAbilityEvent, AbilityEffectContainerComponent>> _filter = default;
        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _abilityEffectPool = default;
        readonly EcsPoolInject<EffectComponent> _effectPool = default;


        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var resolveComp = ref _resolvePool.Value.Get(entity);
                ref var abilityEffectContainer = ref _abilityEffectPool.Value.Get(entity);

                foreach (var abilityEffect in abilityEffectContainer.AbilityEffects)
                {
                    abilityEffect.AddEffect(_world.Value, resolveComp.ResolveEntity);

                    _effectPool.Value.Add(resolveComp.ResolveEntity);
                }
            }
        }
    }
}