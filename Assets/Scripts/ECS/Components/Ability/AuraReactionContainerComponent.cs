using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Реакции ауры, висящие на entity чары. Пока чара на доске —
    /// AuraReactionDispatcherSystem прогоняет их по соответствующим игровым событиям.
    /// Хранит List&lt;IAuraReaction&gt; — ECS-слой не зависит от Model.Ability.
    /// </summary>
    public struct AuraReactionContainerComponent
    {
        public List<IAuraReaction> Reactions;
    }
}
