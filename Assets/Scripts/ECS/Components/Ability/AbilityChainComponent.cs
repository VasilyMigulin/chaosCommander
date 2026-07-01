using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Помечает ability-сущность как СОСТАВНУЮ (цепочечную) и держит её стадии. Ставит AbilityChain.OnInit.
    /// Наличие этого компонента переключает поток: после правил вместо обычного таргетинга стартует
    /// цепочка (RunChainSystem). Стадии (ChainStage) — данные в Shared.Interface.
    /// </summary>
    public struct AbilityChainComponent
    {
        public ChainStage[] Stages;
    }
}
