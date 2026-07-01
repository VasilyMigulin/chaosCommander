namespace Game.Core.Ecs.Components
{
    // === struct (State, транзиентный) ===
    /// <summary>
    /// Способность ждёт выбора цели игроком (Selection == Selected). PlayerEntity — кто выбирает.
    /// Chosen — уже выбранные цели (для Count > 1). RunAbilityTargetSelectionSystem подсвечивает
    /// валидные цели, ловит клик и, набрав Count, кладёт AbilityQueuedState{Targets}.
    /// </summary>
    public struct AbilityTargetPendingState
    {
        public int PlayerEntity;
        public int[] Chosen;
    }
}
