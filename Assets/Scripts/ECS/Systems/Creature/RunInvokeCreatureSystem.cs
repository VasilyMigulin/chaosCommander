using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunInvokeCreatureSystem : IEcsRunSystem 
    {
        readonly EcsWorldInject _world = default; 
        readonly EcsFilterInject<Inc<InvokeEvent>> _filter = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                
            }
        }
    }
}