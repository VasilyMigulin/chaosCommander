using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Счётчик сыгранных за матч карт по ModelId — висит на entity игрока,
    /// инкрементится MatchCounterTrackerSystem на CardPlayedEvent.
    /// Используется «Грыз» (+1/1 за каждого Чёрта), «Позвать рой» (умножает число пауков
    /// на число прошлых сыгранных Позвать-рой), «Гнидальф» (через ручную регистрацию).
    /// </summary>
    public struct MatchCounterComponent
    {
        public Dictionary<int, int> CountsByModelId;
    }
}
