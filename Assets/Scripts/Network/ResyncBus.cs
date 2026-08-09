namespace Game.Core.Network
{
    /// <summary>
    /// Статический мост self-heal ресинка (паттерн ActionQueue): RPC-приём пишет (возможно из другого потока),
    /// WorldResyncSystem читает в главном. Запрос снэпшота (пассив → актив) и собранный чанкером байт-снэпшот
    /// (актив → пассив). Чистится ClearForBattle() на старте боя (вместе с NetHealth).
    /// </summary>
    public static class ResyncBus
    {
        static readonly object _lock = new object();
        static bool   _snapshotRequested;
        static byte[] _incomingSnapshot;

        /// <summary>Пир запросил снэпшот мира (мы — авторитет/актив).</summary>
        public static void NoteSnapshotRequested()
        {
            lock (_lock) _snapshotRequested = true;
        }

        public static bool TryConsumeSnapshotRequest()
        {
            lock (_lock)
            {
                if (!_snapshotRequested) return false;
                _snapshotRequested = false;
                return true;
            }
        }

        /// <summary>Полный снэпшот собран из чанков (мы — пассив, применяем).</summary>
        public static void DeliverSnapshot(byte[] data)
        {
            lock (_lock) _incomingSnapshot = data;
        }

        public static bool TryTakeSnapshot(out byte[] data)
        {
            lock (_lock)
            {
                data = _incomingSnapshot;
                _incomingSnapshot = null;
                return data != null;
            }
        }

        public static void ClearForBattle()
        {
            lock (_lock) { _snapshotRequested = false; _incomingSnapshot = null; }
        }
    }
}
