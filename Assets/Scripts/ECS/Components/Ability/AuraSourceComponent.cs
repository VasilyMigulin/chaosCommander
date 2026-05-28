namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Постоянный модификатор статов (аура). Висит на entity способности с триггером Aura.
    /// Применяется, пока карта-источник на поле (BoardTag).
    /// Фильтр целей берётся из AbilityTargetFlagsComponent той же способности
    /// (AllyCreature / EnemyCreature / ExcludeSelf).
    /// Пересчитывается каждый кадр в AuraRecalcSystem.
    /// </summary>
    public struct AuraSourceComponent
    {
        public int AttackBonus;
        public int HealthBonus;
    }
}
