namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Глобальный статус матча. IsOver=true после того как GameOverCheckSystem зафиксировал
    /// победу/поражение/ничью — турн-системы встают, новый ход не выдаётся. Сброс в
    /// EcsRunHandler.Dispose (паттерн как CastMultiplierService).
    /// </summary>
    public static class MatchState
    {
        public static bool IsOver;

        public static void Clear() => IsOver = false;
    }
}
