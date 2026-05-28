using System.Collections.Generic;

namespace Game.Core.Network
{
    /// <summary>
    /// Выбор при раскопке (excavate), пришедший от активного игрока, до момента когда
    /// пассивная сторона воспроизведёт соответствующий каст.
    /// Ключ — NetworkEntityKey карты-источника.
    ///
    /// Заполняется ReplayActionSystem (из ActionCardPickedData), читается
    /// CardPickSelectionSystem при авто-разрешении выбора для вражеского каста.
    /// </summary>
    public static class CardPickReplayStore
    {
        public struct PickChoice
        {
            /// <summary>NetworkEntityKey выбранной карты (существующей или создаваемой).</summary>
            public string ChosenEntityKey;
            /// <summary>true — карту нужно создать из данных пула.</summary>
            public bool CreateFromPool;
            /// <summary>Для CreateFromPool: ExpansionId создаваемой карты.</summary>
            public string ExpansionId;
            /// <summary>Для CreateFromPool: CardId (ModelId) создаваемой карты.</summary>
            public int CardId;
        }

        private static readonly Dictionary<string, PickChoice> _picks = new Dictionary<string, PickChoice>();
        private static readonly object _lock = new object();

        public static void Set(string sourceKey, PickChoice choice)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            lock (_lock) _picks[sourceKey] = choice;
        }

        public static bool TryPeek(string sourceKey, out PickChoice choice)
        {
            choice = default;
            if (string.IsNullOrEmpty(sourceKey)) return false;
            lock (_lock) return _picks.TryGetValue(sourceKey, out choice);
        }

        public static void Remove(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            lock (_lock) _picks.Remove(sourceKey);
        }

        public static void Clear()
        {
            lock (_lock) _picks.Clear();
        }
    }
}
