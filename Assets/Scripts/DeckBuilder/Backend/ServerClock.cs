using System;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Серверное время PlayFab на клиенте — ТОЛЬКО для отображения (таймеры сброса, обратный отсчёт).
    /// Авторитет всех сбросов (daily/weekly/login/rotation) — на сервере (Azure Functions по своему UTC).
    /// Клиентские часы не доверяем: считаем offset от device-времени в момент синка и накладываем его.
    ///
    /// Синхронизировать один раз после логина (BackendSession).
    /// </summary>
    public static class ServerClock
    {
        static TimeSpan _offset = TimeSpan.Zero;
        public static bool Synced { get; private set; }

        /// <summary>Текущее серверное UTC-время (device UTC + offset). До синка = device UTC.</summary>
        public static DateTime NowUtc => DateTime.UtcNow + _offset;

        /// <summary>Запросить серверное время и вычислить offset. Идемпотентно.</summary>
        public static void Sync(Action onDone = null, Action<string> onError = null)
        {
            PlayFabClientAPI.GetTime(new GetTimeRequest(),
                result =>
                {
                    _offset = result.Time - DateTime.UtcNow;
                    Synced = true;
                    onDone?.Invoke();
                },
                error =>
                {
                    Debug.LogWarning($"[ServerClock] GetTime failed: {error.ErrorMessage}");
                    onError?.Invoke(error.ErrorMessage);
                });
        }

        // ── Хелперы для UI-таймеров ────────────────────────────────────────────

        /// <summary>Ближайший момент ежедневного сброса (в UTC) при заданном часе сброса.</summary>
        public static DateTime NextDailyResetUtc(int resetHourUtc)
        {
            var now = NowUtc;
            var todayReset = new DateTime(now.Year, now.Month, now.Day, resetHourUtc, 0, 0, DateTimeKind.Utc);
            return now < todayReset ? todayReset : todayReset.AddDays(1);
        }

        /// <summary>Ближайший момент еженедельного сброса (в UTC): заданный день недели + час.</summary>
        public static DateTime NextWeeklyResetUtc(DayOfWeek resetDay, int resetHourUtc)
        {
            var now = NowUtc;
            int daysAhead = ((int)resetDay - (int)now.DayOfWeek + 7) % 7;
            var candidate = new DateTime(now.Year, now.Month, now.Day, resetHourUtc, 0, 0, DateTimeKind.Utc)
                            .AddDays(daysAhead);
            return now < candidate ? candidate : candidate.AddDays(7);
        }

        /// <summary>Остаток до момента (для обратного отсчёта). Не отрицательный.</summary>
        public static TimeSpan TimeUntil(DateTime utc)
        {
            var d = utc - NowUtc;
            return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        }
    }
}
