namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Детерминированная «втасовка» карты в колоду. Индекс вставки вычисляется из NetworkEntityKey карты
    /// (FNV-1a хеш): он ОДИНАКОВ на обоих клиентах → порядок колоды остаётся синхронным БЕЗ передачи позиции
    /// и без общего сида, но для игрока позиция непредсказуема (настоящая «втасовка», а не «в конец»).
    ///
    /// Почему так, а не Random: UnityEngine.Random не синхронен между клиентами, а общий сид тут не выйдет
    /// (клиенты делают разное число вызовов Random на старте/мулигане → расходятся). Хеш ключа — детерминирован
    /// при нуле передачи. Безопасно при draw-by-key и сохраняет валидность эффектов «верх колоды».
    /// </summary>
    public static class DeckShuffleUtil
    {
        /// <summary>Индекс вставки в список колоды длиной count: 0..count включительно.</summary>
        public static int InsertIndex(string netKey, int count)
        {
            if (count <= 0) return 0;
            uint h = 2166136261u;                 // FNV-1a (стабильный, кросс-платформенный)
            if (!string.IsNullOrEmpty(netKey))
                for (int i = 0; i < netKey.Length; i++) { h ^= netKey[i]; h *= 16777619u; }
            return (int)(h % (uint)(count + 1));
        }
    }
}
