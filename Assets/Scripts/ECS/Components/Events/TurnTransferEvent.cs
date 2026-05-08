namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Команда от хоста: начать ход следующего игрока.
    /// Бросается на entity нового активного игрока по RPC от хоста.
    /// </summary>
    public struct TurnTransferEvent
    {
        public int FromPlayerId;
        public int ToPlayerId;
        public int TurnNumber;
        public int PersonalTurnNumber;
    }
}
