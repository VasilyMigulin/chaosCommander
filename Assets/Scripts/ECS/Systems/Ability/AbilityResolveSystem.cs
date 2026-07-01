using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems 
{
    public sealed class AbilityResolveSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;  

        public void Init(IEcsSystems systems)
        { 
        }

        public void Run (IEcsSystems systems) 
        { 
        }
        public void Destroy(IEcsSystems systems)
        { 
        }

    }
}