using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Канал синка НЕдетерминированной генерации карт (GainRandomCardEffect: случайный выбор из пула).
    /// Активный РЕКОРДИТ выбранные идентичности (Sent) — они едут в снапшоте стадии; пассив загружает их
    /// в Replay-очередь и эффект берёт оттуда вместо случайного ролла. Аналог CardPickReplayStore.
    /// ECS однопоточный, применение по одному → статик безопасен.
    /// </summary>
    public static class GeneratedCardChannel
    {
        // Актив: накопленные за текущую стадию выборы (exp, cardId).
        public static readonly List<(string exp, int cardId)> Sent = new();

        public static void ClearSent() => Sent.Clear();
        public static void Record(string exp, int cardId) => Sent.Add((exp, cardId));

        // Пассив: очередь воспроизведения из снапшота.
        static readonly Queue<(string exp, int cardId)> _replay = new();

        public static void LoadReplay(string[] exps, int[] ids)
        {
            _replay.Clear();
            if (exps == null || ids == null) return;
            int n = System.Math.Min(exps.Length, ids.Length);
            for (int i = 0; i < n; i++) _replay.Enqueue((exps[i], ids[i]));
        }

        public static void ClearReplay() => _replay.Clear();

        public static bool TryReplay(out string exp, out int cardId)
        {
            if (_replay.Count > 0) { var t = _replay.Dequeue(); exp = t.exp; cardId = t.cardId; return true; }
            exp = null; cardId = -1; return false;
        }
    }
}
