using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Одно правило активации способности — чистая проверка по состоянию мира.
    /// Правила НЕ контейнер: их список лежит в AbilityRuleContainerComponent на ability-сущности,
    /// прогоняет RunCheckAbilityRulesSystem (cardEntity/playerEntity берёт из AbilityCastEvent).
    /// </summary>
    public interface IRule
    {
        bool Evaluate(EcsWorld world, int cardEntity, int playerEntity);
    }
}
