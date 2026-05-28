using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunStartMatchCardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<MatchStartEvent, AbilityContainerComponent>> _filter = default;
        readonly EcsPoolInject<MatchStartEvent> _matchStartPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var abilityContainerComp = ref _abilityContainerPool.Value.Get(entity);

                foreach (var abilityEntity in abilityContainerComp.AbilityEntities)
                {
                    _matchStartPool.Value.Add(abilityEntity);
                }
            }
        }
    }
}