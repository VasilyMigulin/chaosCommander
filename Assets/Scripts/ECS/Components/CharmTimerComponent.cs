namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Таймер жизни чары в ходах владельца. Тикает в КОНЦЕ хода владельца (CharmTimerTickSystem);
    /// при TurnsRemaining ≤ 0 чара уничтожается (CharmDieSystem → CreatureDiedEvent + на кладбище).
    /// Вешается только если CardCharmModel.TurnsAlive > 0; 0 = постоянная чара (компонента нет).
    /// </summary>
    public struct CharmTimerComponent
    {
        public int TurnsRemaining;
    }
}
