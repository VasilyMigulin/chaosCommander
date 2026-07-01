namespace Game.Core.Ecs.Components
{
    public enum LeaveDestination { Hand, Deck, Grave, Limbo }

    // === struct (Event) ===
    /// <summary>
    /// Запрос «существо уходит с поля» в зону Destination (баунс/баниш/замешивание). Вешается на существо;
    /// RunLeaveBoardSystem снимает с борда (теги/позиция/визуал), кладёт в зону и обновляет списки.
    /// В отличие от смерти НЕ публикует CreatureDiedEvent (это не гибель) — OnDie не срабатывает.
    /// </summary>
    public struct LeaveBoardEvent
    {
        public LeaveDestination Destination;
    }
}
