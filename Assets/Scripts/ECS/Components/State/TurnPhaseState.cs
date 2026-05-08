namespace Game.Core.Ecs.Components
{
    public enum TurnPhase
    {
        // --- Фазы которыми командует хост ---
        TurnStartAbilities,   // хост дал команду: отрабатываем OnTurnStart способности
        PlayerTurn,           // хост дал команду: активный игрок ходит, таймер тикает
        TurnEndAbilities,     // хост дал команду: отрабатываем OnTurnEnd способности

        // --- Локальные фазы ожидания ---
        WaitingForTurnStart,  // хост ещё не дал команду начать ход (мы не активный клиент или ждём RPC)
        WaitingForHostAck,    // ждём подтверждения от хоста после отправки NotifyReady
    }

    /// <summary>
    /// Вешается на сущность КАЖДОГО игрока (не только активного).
    /// Хранит текущую фазу хода с точки зрения этого клиента.
    /// TurnState по-прежнему только у активного игрока.
    /// </summary>
    public struct TurnPhaseState
    {
        public TurnPhase Phase;
    }
}
