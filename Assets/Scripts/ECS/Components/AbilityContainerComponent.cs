namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит entity-идентификаторы способностей карты в порядке их выполнения.
    /// Каждый элемент — отдельная ECS-сущность способности.
    /// </summary>
    public struct AbilityContainerComponent
    {
        public int[] AbilityEntities;
    }
}
