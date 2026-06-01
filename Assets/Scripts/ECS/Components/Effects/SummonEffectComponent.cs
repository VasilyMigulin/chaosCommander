namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Призывает существа (или токены) указанной модели от имени владельца способности.
    /// ApplySummonSystem ищет пустые клетки чередуясь L/R от источника
    /// (если источник на доске) или от центра аватара владельца (если источник —
    /// чары/заклинание). При отсутствии места обычные карты идут в кладбище,
    /// токены — исчезают.
    /// </summary>
    public struct SummonEffectComponent
    {
        public string ExpansionId;
        public int CardId;

        /// <summary>Фиксированное количество призывов (игнорируется при FillRow=true).</summary>
        public int Count;

        /// <summary>Призывать пока есть свободные клетки в ряду источника/аватара.</summary>
        public bool FillRow;

        /// <summary>true — призывается токен (исчезает при нехватке места); false — обычная карта (уходит в кладбище).</summary>
        public bool IsToken;

        /// <summary>
        /// Если ≥ 0 — Count заменяется на счётчик MatchCounterComponent у владельца способности
        /// для указанной модели (Позвать рой: counter("Позвать рой")).
        /// </summary>
        public int CountFromCounterModelId;
    }
}
