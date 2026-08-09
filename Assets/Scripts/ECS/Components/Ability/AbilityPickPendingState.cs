namespace Game.Core.Ecs.Components
{
    // === struct (State, транзиентный) ===
    /// <summary>
    /// Способность ждёт выбор цели из НЕ-Board зоны (колода/рука/кладбище) через окно выбора
    /// (CardPickOfferedEvent → PickupWindow → CardPickChosenEvent), а не клики по клеткам доски.
    /// PlayerEntity — кто выбирает. Offered — окно уже показано. Chosen — накопленные (для Count>1).
    /// Ставит RunAbilityTargetingSystem; ведёт RunAbilityPickSelectionSystem → AbilityQueuedState.
    /// </summary>
    public struct AbilityPickPendingState
    {
        public int PlayerEntity;
        public bool Offered;
        public int[] Chosen;

        /// <summary>Токен окна выбора (PickRequestId) — ключ корреляции CardPickChosenEvent. Перевыдаётся
        /// на КАЖДОЕ окно: при Count>1 способность просит несколько карт подряд, и каждый показ берёт
        /// у брокера свой слот заново.</summary>
        public int RequestId;
    }
}
