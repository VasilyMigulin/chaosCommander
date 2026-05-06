namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// ECS-событие клика по клетке поля. Создаётся BattleState из CellSelectedEvent GameEventBus.
    /// </summary>
    public struct CellClickEvent
    {
        public int Row;
        public int Col;
        public int OwnerId;
    }
}
