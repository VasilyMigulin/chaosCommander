namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер на ИГРОКЕ «не получает доход золота в начале хода» (Наступающий кризис). Вешается через
    /// AddBuffEffect{ BuffMarker{ GoldBlockMarker } } (как ReflectDamageMarker), Tracked=true с источником-чарой:
    /// откат при смерти чары (CharmDieSystem → CreatureDiedEvent → AddBuffEffect.RevertAll). RunTurnStartSystem
    /// пропускает начисление золота игроку с этим маркером. «Оба игрока» = чара вешает маркер на обе player-
    /// сущности. СИНК: AddBuffEffect ре-ранится на обоих, смерть чары синкается (ActionDeathData) → откат зеркальный.
    /// </summary>
    public struct GoldBlockComponent { }
}
