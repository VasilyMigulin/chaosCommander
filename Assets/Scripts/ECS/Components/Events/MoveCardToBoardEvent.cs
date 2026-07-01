namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Переместить карту на доску. Снимает HandTag/DeckTag/GraveTag,
    /// удаляет из HandComponent владельца, ставит BoardTag.
    /// Если Row/Col/OwnerId не -1 — дополнительно пишет BoardPositionComponent
    /// (для существ; чары идут без позиции).
    /// </summary>
    public struct MoveCardToBoardEvent
    {
        public int Row;
        public int Col;
        public int OwnerId;
    }
}
