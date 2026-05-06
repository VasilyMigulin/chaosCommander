using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunResolveAbilityFieldSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<ResolveAbilityEvent>> _filter = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                
            }
        }
    }
}