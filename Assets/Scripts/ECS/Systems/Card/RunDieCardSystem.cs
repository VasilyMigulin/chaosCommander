using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunDieCardSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<DieEvent, AbilityContainerComponent>> _filter = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<DieEvent> _diePool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var abilityContainerComp = ref _abilityContainerPool.Value.Get(entity);

                foreach (var abilityEntity in abilityContainerComp.AbilityEntities)
                {
                    _diePool.Value.Add(abilityEntity);
                }
            }
        }
    }
}