namespace Game.Core.Ecs.Components
{
    // === struct (State, транзиентный) ===
    /// <summary>
    /// Способность прошла правила и ждёт этапа таргетинга. Ставит RunCheckAbilityRulesSystem,
    /// снимает RunAbilityTargetingSystem (после вычисления целей → AbilityQueuedState),
    /// либо переводит в AbilityTargetPendingState для Selected (выбор игроком).
    /// </summary>
    public struct AbilityTargetingState { }
}
