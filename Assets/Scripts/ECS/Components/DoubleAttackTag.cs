namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Свойство «Двойной удар» (аналог Windfury): существо может атаковать ДВАЖДЫ за ход (лимит
    /// MaxAttacksPerTurn поднимается с 1 до 2 — оставшаяся скорость всё равно гейтит фактическую вторую
    /// атаку). Маркер, без состояния — навешивается/снимается DoubleAttackProperty.
    /// </summary>
    public struct DoubleAttackTag { }
}
