namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Событие на сущности игрока: передать ход следующему.
    /// Бросается сервером после завершения OnTurnEnd способностей.
    /// </summary>
    public struct TurnTransferEvent
    {
        public int FromPlayerId;
        public int ToPlayerId;
    }
}
