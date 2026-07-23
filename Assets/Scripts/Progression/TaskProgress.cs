using System.Collections.Generic;

namespace Game.Core.Progression
{
    /// <summary>
    /// Аккумулятор прогресса задач за матч. Трекеры НЕ бьют сервер на каждое событие: урон/карты идут пачками —
    /// это и спам-вызовы, и ГОНКА за журнал (N параллельных read-modify-write в UserReadOnlyData затирали бы
    /// друг друга → потерянный прогресс). Поэтому копим локально и сбрасываем ОДНИМ батч-вызовом в конце матча
    /// (TaskTrackingService.Flush → DailyService.ReportProgressBatch).
    /// </summary>
    internal static class TaskProgress
    {
        static readonly Dictionary<string, int> _pending = new();

        /// <summary>Добавить прогресс типу (суммируется до флеша).</summary>
        public static void Add(string type, int amount)
        {
            if (string.IsNullOrEmpty(type) || amount <= 0) return;
            _pending.TryGetValue(type, out var cur);
            _pending[type] = cur + amount;
        }

        /// <summary>Забрать накопленное и очистить. null, если копить нечего (флеш пропускается).</summary>
        public static Dictionary<string, int> TakeAll()
        {
            if (_pending.Count == 0) return null;
            var copy = new Dictionary<string, int>(_pending);
            _pending.Clear();
            return copy;
        }
    }
}
