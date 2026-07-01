namespace Game.Core.Network
{
    /// <summary>
    /// Генератор коротких уникальных NetworkEntityKey вида "{clientPrefix}-{counter}" (напр. "1-0").
    /// Заменяет 36-символьный Guid → ~4 символа, чтобы снапшоты с множеством целей влезали в лимит RPC
    /// Fusion (512 байт): «Призвать ураган» на 9 целей был 531 байт (перелив → RPC не отправлялся → десинк).
    ///
    /// Уникальность без коллизий:
    ///   • prefix = id ЛОКАЛЬНОГО клиента (разводит ключи двух клиентов) — ставится один раз в InitPlayerSystem;
    ///   • counter монотонен внутри клиента (каждый ключ этого клиента уникален).
    /// Ключ генерит ОДИН клиент и транслирует оппоненту (RPC_SyncDeckSnapshot для колоды; снапшоты резолва
    /// для discover/pick), поэтому формат на детерминизм не влияет — оппонент берёт присланную строку.
    ///
    /// NB: сгенерированные эффектами карты используют отдельную детерминированную схему "{casterKey}~gen{N}"
    /// (GeneratedCardCounterComponent), здесь не участвуют.
    /// </summary>
    public static class NetKey
    {
        static string _prefix = "0";
        static int    _counter;

        /// <summary>Один раз при инициализации локального игрока: задаёт префикс клиента и сбрасывает счётчик
        /// на новый матч (мир пересоздаётся каждый матч, ключи нужны уникальными только в пределах матча).</summary>
        public static void SetClientPrefix(int localPlayerId)
        {
            _prefix  = localPlayerId.ToString();
            _counter = 0;
        }

        /// <summary>Следующий уникальный ключ сущности, созданной на этом клиенте.</summary>
        public static string Next() => _prefix + "-" + _counter++;
    }
}
