using System.Collections.Generic;

namespace Game.Core.Network
{
    /// <summary>
    /// Статическая очередь входящих действий от оппонента.
    /// Заполняется в PhotonRunHandler.RPC_SendActionSnapshot (на стороне получателя).
    /// Опустошается ReplayActionSystem каждый кадр.
    /// </summary>
    public static class ActionQueue
    {
        private static readonly Queue<IActionData> _queue = new Queue<IActionData>();
        private static readonly object _lock = new object();

        /// <summary>Добавить действие в очередь (вызывается из RPC, может быть из другого потока).</summary>
        public static void Enqueue(IActionData action)
        {
            lock (_lock)
                _queue.Enqueue(action);
        }

        /// <summary>Забрать следующее действие. Возвращает false если очередь пуста.</summary>
        public static bool TryDequeue(out IActionData action)
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    action = default;
                    return false;
                }
                action = _queue.Dequeue();
                return true;
            }
        }

        /// <summary>Количество действий в очереди.</summary>
        public static int Count
        {
            get { lock (_lock) return _queue.Count; }
        }

        /// <summary>Очистить очередь (например, при завершении матча).</summary>
        public static void Clear()
        {
            lock (_lock)
                _queue.Clear();
        }
    }
}
