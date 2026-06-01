namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Гейт для RunResolveAbilityEffectSystem: эффекты текущего шага ещё не созданы.
    /// Добавляется AbilityChainAdvanceSystem (при входе и после завершения шага),
    /// снимается RunResolveAbilityEffectSystem после спауна effect-entity.
    /// </summary>
    public struct NeedsStepResolveTag { }
}
