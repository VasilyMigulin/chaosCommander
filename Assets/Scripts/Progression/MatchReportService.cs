using Game.Core.Backend;
using Game.Core.Events;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Progression
{
    /// <summary>
    /// Отчёт об исходе MP-матча на сервер (рейтинг/Elo). Паттерн TaskTrackingService: персистентная
    /// подписка на MatchEndedEvent, живёт вне ECS (GameEventBus.Clear в конце боя её не снимает).
    ///
    /// Гейты: PvE/сюжет/туториал не отчитываются (PveMode.Enabled или MatchIdentity не собрана —
    /// reveal-обмен PlayFabId идёт только в Photon-пайплайне). Идентичность захватывается и чистится
    /// СРАЗУ (до сетевого вызова) — повторный MatchEndedEvent того же матча не продублирует отчёт.
    ///
    /// Джиттер: проигравший (при ничьей — лексикографически больший PlayFabId) шлёт с задержкой ~2с,
    /// чтобы отчёты не столкнулись на сервере (в Classic CloudScript нет CAS — оба получили бы Pending).
    /// Ретраи Pending и обновление PlayerRating — внутри RatingService.Report.
    /// </summary>
    public static class MatchReportService
    {
        const int LoserJitterMs = 2000;

        static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            GameEventBus.SubscribePersistent<MatchEndedEvent>(OnMatchEnded);
        }

        static void OnMatchEnded(MatchEndedEvent e)
        {
            if (PveMode.Enabled) return;
            if (!MatchIdentity.IsSet)
            {
                Debug.Log("[Rating] MatchEnded без идентичности матча (PvE/туториал/соперник без логина) — отчёт не шлётся");
                return;
            }

            string matchId = MatchIdentity.MatchId;
            string oppId   = MatchIdentity.OpponentPlayFabId;
            MatchIdentity.Clear();   // одноразовость: второй MatchEndedEvent того же матча не продублирует отчёт

            string outcome = e.LocalResult switch
            {
                MatchResult.Win  => "win",
                MatchResult.Lose => "lose",
                _                => "draw",
            };

            bool delayed = e.LocalResult == MatchResult.Lose ||
                           (e.LocalResult == MatchResult.Draw &&
                            string.CompareOrdinal(MatchIdentity.LocalPlayFabId ?? "", oppId) > 0);

            Debug.Log($"[Rating] отчёт об исходе: {outcome} (match={matchId}{(delayed ? ", с джиттером" : "")})");

            if (delayed) ReportDelayed(matchId, oppId, outcome);
            else         Report(matchId, oppId, outcome);
        }

        static void Report(string matchId, string oppId, string outcome)
            => RatingService.Report(matchId, oppId, outcome,
                onApplied: resp => GameEventBus.Publish(new RatingUpdatedEvent
                {
                    NewMmr       = resp.Mmr,
                    Delta        = resp.Delta,
                    RewardCode   = resp.RewardCode,
                    RewardAmount = resp.RewardAmount,
                }));

        static async void ReportDelayed(string matchId, string oppId, string outcome)
        {
            await System.Threading.Tasks.Task.Delay(LoserJitterMs);
            Report(matchId, oppId, outcome);
        }
    }
}
