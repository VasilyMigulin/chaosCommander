namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Кулдаун командира после гибели. Висит на entity карты командира (которая
    /// вернулась в руку). Пока компонент есть — командир не может быть разыгран.
    /// Тикает в начале хода владельца (TurnStartResourceSystem); при достижении 0 снимается.
    /// </summary>
    public struct CommanderCooldownComponent
    {
        public int TurnsRemaining;
    }
}
