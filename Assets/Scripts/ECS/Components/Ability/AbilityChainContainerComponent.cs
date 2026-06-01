using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит шаги-цепочки способности (после основных Effects). Шаг 0 — это
    /// Effects/Target/Mode/Shape самой абилки; шаги 1..N — Steps[0..N-1].
    /// Висит на entity способности, если у абилки задана ChainSteps.
    ///
    /// Использует IChainStep (Shared.Interface) — слой данных не зависит от Model.Ability;
    /// модели лишь реализуют интерфейс при клонировании в Ability.Init.
    /// </summary>
    public struct AbilityChainContainerComponent
    {
        public List<IChainStep> Steps;
    }
}
