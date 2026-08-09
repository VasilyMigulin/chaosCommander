using System;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Рейтинг (MMR) — серверный авторитет (PlayFab statistics, Elo считает CloudScript).
    /// Клиент: Fetch после логина (BackendSession) кладёт серверное значение в PlayerRating
    /// (PlayerPrefs остаётся оффлайн-кэшем — им же живёт подбор в MatchmakingService);
    /// Report в конце MP-матча (MatchReportService) шлёт исход на взаимное подтверждение.
    ///
    /// Report ретраит Pending: сервер применяет Elo, только когда пришли ОБА отчёта, и при
    /// гонке одновременных вызовов оба могут получить Pending — повтор через пару секунд
    /// доводит расчёт (второй отчёт к тому моменту уже записан).
    /// </summary>
    public static class RatingService
    {
        // ── DTO контракта ──────────────────────────────────────────────────────

        [Serializable]
        public class RatingData
        {
            public int Mmr;
            public int Wins;
            public int Losses;
        }

        [Serializable]
        public class MatchReportRequest
        {
            public string MatchId;
            public string OpponentPlayFabId;
            public string Outcome;   // "win" / "lose" / "draw"
        }

        [Serializable]
        public class MatchReportResponse
        {
            public bool   Applied;
            public bool   Pending;
            public bool   Conflict;
            public string Reason;
            public int    Mmr;
            public int    Delta;
            // Золото за матч (Title Data ratingConfig.matchReward, выдаёт сервер при расчёте).
            public string RewardCode;
            public int    RewardAmount;
            public System.Collections.Generic.List<CurrencyAmount> Wallet;   // балансы после выдачи
        }

        const int ReportRetries = 2;
        const int ReportRetryDelayMs = 3000;

        // ── Вызовы ───────────────────────────────────────────────────────────

        /// <summary>Подтянуть серверный MMR в PlayerRating. Ошибка не критична — остаётся локальный кэш.</summary>
        public static void Fetch(Action onDone = null)
            => FunctionService.Call<RatingData>(BackendConfig.Fn.GetRating,
                resp =>
                {
                    if (resp != null && resp.Mmr > 0)
                    {
                        PlayerRating.Mmr = resp.Mmr;
                        Debug.Log($"[Rating] серверный MMR: {resp.Mmr} (W{resp.Wins}/L{resp.Losses})");
                    }
                    onDone?.Invoke();
                },
                err => { Debug.LogWarning($"[Rating] Fetch failed: {err}"); onDone?.Invoke(); });

        /// <summary>
        /// Отчёт об исходе матча. onApplied получает полный ответ (MMR/дельта/золото за матч) при
        /// успешном расчёте; Pending после всех ретраев / конфликт / ошибка → onApplied не зовётся
        /// (зависшую пару сервер дорешает лениво при следующем Fetch/Report, см. maintainRatingGroup).
        /// </summary>
        public static void Report(string matchId, string opponentPlayFabId, string outcome,
            Action<MatchReportResponse> onApplied = null)
            => ReportAttempt(matchId, opponentPlayFabId, outcome, ReportRetries, onApplied);

        static void ReportAttempt(string matchId, string opponentPlayFabId, string outcome,
            int retriesLeft, Action<MatchReportResponse> onApplied)
        {
            var req = new MatchReportRequest { MatchId = matchId, OpponentPlayFabId = opponentPlayFabId, Outcome = outcome };
            FunctionService.Call<MatchReportRequest, MatchReportResponse>(
                BackendConfig.Fn.ReportMatchResult, req,
                resp =>
                {
                    if (resp == null) { Debug.LogWarning("[Rating] Report: пустой ответ"); return; }

                    if (resp.Applied)
                    {
                        PlayerRating.Mmr = resp.Mmr;
                        PlayerWallet.ApplyIfPresent(resp.Wallet);
                        Debug.Log($"[Rating] матч рассчитан: MMR {resp.Mmr} ({(resp.Delta >= 0 ? "+" : "")}{resp.Delta}), " +
                                  $"награда {resp.RewardAmount} {resp.RewardCode}");
                        onApplied?.Invoke(resp);
                        return;
                    }

                    if (resp.Conflict)
                    {
                        Debug.LogWarning($"[Rating] отчёты разошлись (конфликт) — рейтинг не изменён");
                        return;
                    }

                    if (resp.Pending && retriesLeft > 0)
                    {
                        Debug.Log($"[Rating] отчёт соперника ещё не пришёл — ретрай через {ReportRetryDelayMs / 1000}с ({retriesLeft})");
                        RetryLater(matchId, opponentPlayFabId, outcome, retriesLeft - 1, onApplied);
                        return;
                    }

                    Debug.Log($"[Rating] отчёт записан, расчёт отложен (pending; reason={resp.Reason ?? "-"})");
                },
                err => Debug.LogWarning($"[Rating] Report failed: {err}"));
        }

        static async void RetryLater(string matchId, string opponentPlayFabId, string outcome,
            int retriesLeft, Action<MatchReportResponse> onApplied)
        {
            await System.Threading.Tasks.Task.Delay(ReportRetryDelayMs);
            ReportAttempt(matchId, opponentPlayFabId, outcome, retriesLeft, onApplied);
        }
    }
}
