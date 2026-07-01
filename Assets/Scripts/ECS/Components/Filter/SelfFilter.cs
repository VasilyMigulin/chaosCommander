using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct SelfFilter : ITriggerFilter
    { 
        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(Service.EntityService.ABILITY_ENTITY, out int ability))
            {
                world.GetPool<SelfFilter>().Add(ability);   
            }
        }
    }
}