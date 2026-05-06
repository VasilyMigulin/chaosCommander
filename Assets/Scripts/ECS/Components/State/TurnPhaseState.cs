namespace Game.Core.Ecs.Components
{
    public enum TurnPhase
    {
        TurnStartAbilities,   // ждём пока отработают OnTurnStart способности
        PlayerTurn,           // игрок активен, таймер тикает
        TurnEndAbilities,     // ждём пока отработают OnTurnEnd способности
        TurnTransfer          // передаём ход (ожидаем подтверждения от следующего клиента)
    }

    /// <summary>
    /// Вешается на сущность активного игрока рядом с TurnState.
    /// Хранит текущую фазу хода.
    /// </summary>
    public struct TurnPhaseState
    {
        public TurnPhase Phase;
    }
}
