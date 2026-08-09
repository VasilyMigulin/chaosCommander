namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Свойство «Скрытый»: TurnsRemaining ходов владельца (тик в начале его хода — StealthTickSystem,
    /// паттерн CreatureTimerComponent) носитель исключён из ВРАЖЕСКОГО таргетинга — не выбирается кликом
    /// игрока (RunSelectCellSystem), не выбирается ИИ как цель атаки (RunAiTurnSystem), не проходит в
    /// TargetGather для способностей чужой стороны. Своя сторона видит и таргетит его как обычно. НЕ прячет
    /// физическое присутствие на доске (occupancy/pathing не меняются — прячется только от прицела, не от
    /// клетки). На 0 компонент просто снимается (не смерть).
    /// </summary>
    public struct StealthComponent
    {
        public int TurnsRemaining;
    }
}
