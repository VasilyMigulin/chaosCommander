using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Predicate-правило: HP игрока-владельца способности строго меньше Threshold.
    /// Чекер крутится каждый кадр.
    /// </summary>
    [System.Serializable]
    public struct OwnerHealthBelowRule : IAbilityRule
    {
        public int Threshold;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.RULE_ENTITY, out int ruleEntity))
                world.GetPool<OwnerHealthBelowRule>().Add(ruleEntity) = this;
        }
    }
}
