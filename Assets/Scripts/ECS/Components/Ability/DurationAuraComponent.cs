namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на чару-источник ауры с ограниченным сроком действия.
    /// DurationAuraTickSystem уменьшает TurnsRemaining в начале хода владельца;
    /// при достижении 0 чара уходит с доски (BoardTag → GraveTag), и AuraRecalcSystem
    /// автоматически перестаёт применять её бонусы со следующего кадра.
    /// </summary>
    public struct DurationAuraComponent
    {
        public int TurnsRemaining;
    }
}
