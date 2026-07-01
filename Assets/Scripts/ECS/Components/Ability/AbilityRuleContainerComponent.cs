using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Контейнер правил активации на ability-сущности — массив составных правил (групп Rule).
    /// RunCheckAbilityRulesSystem требует прохождения ВСЕХ групп (AND между ними);
    /// внутри каждой группы листья IRule объединены по её Mode. Заполняется в Ability.Init.
    /// </summary>
    public struct AbilityRuleContainerComponent
    {
        public Rule[] Rules;
    }
}
