using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит список требований к полю боя, необходимых для розыгрыша карты.
    /// Навешивается на entity карты при её инициализации.
    /// CheckPlayRequirementSystem проверяет их каждый фрейм и управляет тегом PlayableTag.
    /// </summary>
    public struct AbilityPlayRequirementContainerComponent
    {
        public List<IAbilityPlayRequirement> PlayRequirements;
    }
}
