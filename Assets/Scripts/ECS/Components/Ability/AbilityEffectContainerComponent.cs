using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct AbilityEffectContainerComponent 
    {
        public List<IAbilityEffect> AbilityEffects;
    }
}