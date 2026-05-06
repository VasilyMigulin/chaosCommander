using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct AbilityConditionContainerComponent 
    {
        public List<IAbilityCondition> AbilityConditions;
    }
}