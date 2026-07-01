using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Контейнер триггеров на ability-сущности. Триггеры сами подписываются на шину в Init
    /// (его зовёт Ability.Init), поэтому отдельной системы-обработчика нет — компонент держит их
    /// для единообразия/инспекции. Заполняется в Ability.Init.
    /// </summary>
    public struct AbilityTriggerContainerComponent
    {
        public ITrigger[] Triggers;
    }
}
