namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// «Существо умрёт через N своих ходов». Тикается CreatureTimerTickSystem
    /// в начале хода владельца; при достижении 0 — DeadTag.
    /// </summary>
    public struct CreatureTimerComponent
    {
        public int TurnsRemaining;
    }
}
