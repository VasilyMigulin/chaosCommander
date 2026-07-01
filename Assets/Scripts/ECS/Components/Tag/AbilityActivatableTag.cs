namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Висит на ability-сущности когда все её правила прошли (Total == Passed).
    /// Триггер/router/invoke-системы создают AbilityCastEvent только на ability
    /// с этим тегом. Снимается консолидатором когда хоть одно правило падает.
    /// </summary>
    public struct AbilityActivatableTag { }
}
