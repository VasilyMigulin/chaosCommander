using System;
using System.Collections.Generic;

namespace Game.Core.Backend
{
    /// <summary>
    /// Ежедневные/еженедельные задачи + входные награды. Всё считает сервер по серверному времени.
    /// Клиент запрашивает состояние (GetState), клеймит награды и репортит прогресс через функции.
    /// Конфиг задач/наград живёт в Title Data (дизайнер правит без передеплоя).
    /// </summary>
    public static class DailyService
    {
        // ── DTO контракта ──────────────────────────────────────────────────────

        [Serializable]
        public class TaskState
        {
            public string Id;
            public string Type;        // напр. "win_games", "play_cards", "open_boosters"
            public int    Progress;
            public int    Target;
            public bool   Claimable;   // цель достигнута и не заклеймлено
            public bool   Claimed;
            public RewardBundle Reward = new RewardBundle();
        }

        [Serializable]
        public class LoginDay
        {
            public int    Day;
            // Награда КОМПАКТНО: ОДНА иконка + число, без текста. Иконку резолвим единообразно:
            //   RewardCode ("GD"/"GM") → иконка валюты (InterfaceConfig);
            //   иначе RewardItemId ("booster_standard"/"avatar_..."/карта) → InstanceData.Miniature.
            // Отдельного «booster icon» нет — бустер это тот же предмет с миниатюрой.
            public int    RewardAmount;    // сколько
            public string RewardCode;      // валюта; пусто → предмет
            public string RewardItemId;    // предмет; пусто → валюта
            public bool   Claimed;
            public bool   Today;
        }

        [Serializable]
        public class LoginRewardState
        {
            public int  StreakDay;        // текущий день серии (1..N)
            public bool Available;        // доступно к клейму сегодня
            public RewardBundle Today = new RewardBundle();
            public List<LoginDay> Days = new List<LoginDay>();   // 7-дневная лента (может быть пустой)
        }

        [Serializable]
        public class DailyStateResponse
        {
            public string ServerTimeUtc;      // ISO-8601; для синка/дисплея
            public int    DailyResetHourUtc;  // час ежедневного сброса
            public int    WeeklyResetDay;     // 0=Sun..6=Sat (среда=3)
            public int    WeeklyResetHourUtc;
            public LoginRewardState Login = new LoginRewardState();
            public List<TaskState> Daily  = new List<TaskState>();
            public List<TaskState> Weekly = new List<TaskState>();
        }

        [Serializable] public class ClaimTaskRequest      { public string TaskId; }
        [Serializable] public class ReportProgressRequest { public string Type; public int Amount = 1; }
        [Serializable] public class ProgressItem         { public string Type; public int Amount; }
        [Serializable] public class ReportBatchRequest   { public List<ProgressItem> Items = new List<ProgressItem>(); }

        // ── Вызовы ───────────────────────────────────────────────────────────

        public static void GetState(Action<DailyStateResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetDailyState, onSuccess, onError);

        public static void ClaimLoginReward(Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<RewardResponse>(BackendConfig.Fn.ClaimLoginReward,
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);

        public static void ClaimTask(string taskId, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ClaimTaskRequest, RewardResponse>(
                BackendConfig.Fn.ClaimTask, new ClaimTaskRequest { TaskId = taskId },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);

        /// <summary>
        /// Сообщить о прогрессе (напр. после матча: "win_games"+1). Сервер сам инкрементит
        /// подходящие daily/weekly задачи. Прогресс, который сервер и так видит по своим функциям
        /// (open_boosters и т.п.), репортить не нужно.
        /// </summary>
        public static void ReportProgress(string type, int amount = 1,
            Action onSuccess = null, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.ReportProgress,
                new ReportProgressRequest { Type = type, Amount = amount }, onSuccess, onError);

        /// <summary>
        /// Сообщить ПАЧКУ прогресса одним вызовом (сервер: один read-modify-write журнала). Нужно, когда в конце
        /// матча копится несколько типов сразу — N одиночных ReportProgress гонялись бы за один ключ журнала
        /// (параллельные read-modify-write затирают друг друга → потерянный прогресс). Пустой набор — no-op.
        /// </summary>
        public static void ReportProgressBatch(IEnumerable<KeyValuePair<string, int>> items,
            Action onSuccess = null, Action<string> onError = null)
        {
            var req = new ReportBatchRequest();
            if (items != null)
                foreach (var kv in items)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0)
                        req.Items.Add(new ProgressItem { Type = kv.Key, Amount = kv.Value });
            if (req.Items.Count == 0) { onSuccess?.Invoke(); return; }
            FunctionService.Call(BackendConfig.Fn.ReportProgressBatch, req, onSuccess, onError);
        }
    }
}
