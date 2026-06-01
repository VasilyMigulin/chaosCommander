using System.Collections.Generic;
using Game.Core.Service;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Чистый интерфейс шага цепочки эффектов способности.
    /// Модель ChainStep (Game.Core.Model.Ability) реализует его и копирует в
    /// AbilityChainContainerComponent.Steps как IReadOnlyList&lt;IChainStep&gt;.
    /// ECS-слой работает только с интерфейсом и не знает про Model.Ability.
    /// </summary>
    public interface IChainStep
    {
        ChainTargetSource TargetSource { get; }
        TargetShape Shape { get; }
        TargetMask TargetMaskOverride { get; }
        IChainCondition Condition { get; }
        IReadOnlyList<IAbilityEffect> Effects { get; }
    }
}
