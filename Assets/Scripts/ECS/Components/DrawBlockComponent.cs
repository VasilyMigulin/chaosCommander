namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер на ИГРОКЕ «не берёт карту в начале хода» (Проклятье для принцессы) — ПОКА ЖИВ источник.
    /// Как GoldBlockComponent: вешается через AddBuffEffect{ BuffMarker{ DrawBlockMarker } }, Tracked=true
    /// с источником-чарой — откат при смерти чары (CharmDieSystem → CreatureDiedEvent → AddBuffEffect.
    /// RevertAll). RunTurnStartSystem просто пропускает добор игроку с этим маркером, ничего не снимая сам.
    /// </summary>
    public struct DrawBlockComponent { }
}
