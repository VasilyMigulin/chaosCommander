namespace Game.Core.Ecs.Components
{
    /// <summary>Когда у чары списывается ход жизни. Значения ЗАКРЕПЛЕНЫ (как CountSource) — перестановка
    /// сломает заавторенные ассеты; TurnEnd=0 = прежнее поведение всех старых чар.</summary>
    public enum CharmTickMoment
    {
        /// <summary>В КОНЦЕ хода владельца. Для чар, работающих «в конце хода» или постоянным эффектом:
        /// TurnsAlive=1 — «до конца этого хода», 2 — «переживёт ход оппонента».</summary>
        TurnEnd = 0,

        /// <summary>В НАЧАЛЕ хода владельца, ПОСЛЕ рассылки OnTurnStart (тик идёт в Run системы, а
        /// триггеры помечаются синхронно на самом событии — эффект успевает сработать). Для чар с
        /// эффектом «в начале хода»: TurnsAlive=1 — «сработает ровно один раз, в мой следующий ход»
        /// (при TurnEnd такая чара умирала, ни разу не отработав — ради этого момент и добавлен).</summary>
        TurnStart = 1,
    }

    // === struct (Component) ===
    /// <summary>
    /// Таймер жизни чары в ходах владельца. Тикает в момент Moment (CharmTimerTickSystem);
    /// при TurnsRemaining ≤ 0 чара уничтожается (CharmDieSystem → CreatureDiedEvent + на кладбище).
    /// Вешается только если CardCharmModel.TurnsAlive > 0; 0 = постоянная чара (компонента нет).
    /// </summary>
    public struct CharmTimerComponent
    {
        public int TurnsRemaining;
        public CharmTickMoment Moment;
    }
}
