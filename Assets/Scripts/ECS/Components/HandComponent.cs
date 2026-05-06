namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит entity-идентификаторы карт в руке игрока.
    /// </summary>
    public struct HandComponent
    {
        public const int MaxHandSize = 6;
        public int[] CardEntities;
        public int Count;
    }
}
