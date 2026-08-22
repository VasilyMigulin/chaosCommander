namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// «Карта в руке будет сброшена через N своих ходов» (Сделка с чертом). Тикается
    /// HandDiscardTimerTickSystem в начале хода владельца, пока карта в руке; при
    /// TurnsRemaining ≤ 0 — сброс (AltCostUtil.Discard). Карту, разыгранную раньше срока,
    /// таймер уже не видит (фильтр требует HandTag) — тикать нечего, как и задумано.
    /// </summary>
    public struct HandDiscardTimerComponent
    {
        public int TurnsRemaining;
    }
}
