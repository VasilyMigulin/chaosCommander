using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct BoardZoneRule : IAbilityRule
    {
        // add your data here.
        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                world.GetPool<BoardZoneRule>().Add(ability) = new BoardZoneRule()
                {

                };
            }
        }
    }
}