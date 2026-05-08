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
        readonly EcsPoolInject<AbilityConditionContainerComponent> _conditionContainerPool = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var abilityContainerComp = ref _abilityContainerPool.Value.Get(entity);

                foreach (var abilityEntity in abilityContainerComp.AbilityEntities)
                {
                    if (_conditionContainerPool.Value.Has(abilityEntity))
                    {
                        ref var condContainer = ref _conditionContainerPool.Value.Get(abilityEntity);
                        if (condContainer.AbilityConditions != null)
                        {
                            foreach (var condition in condContainer.AbilityConditions)
                                condition.Dispose();
                        }
                    }

                    _diePool.Value.Add(abilityEntity);
                }
            }
        }
    }
}