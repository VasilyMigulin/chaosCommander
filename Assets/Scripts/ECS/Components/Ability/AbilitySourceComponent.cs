namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Обратная ссылка с entity способности на карту-источник и индекс способности
    /// в её AbilityContainerComponent. Используется для детерминированного (одинакового
    /// на обоих клиентах) seed выбора случайной цели — через NetworkEntityKey карты,
    /// а не через локальный entity id (который различается между клиентами).
    /// </summary>
    public struct AbilitySourceComponent
    {
        public int CardEntity;
        public int AbilityIndex;
    }
}
