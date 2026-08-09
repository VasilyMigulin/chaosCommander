namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Идентичность ТЕКУЩЕГО MP-матча для отчёта о рейтинге (RatingService.Report):
    /// GUID матча (генерирует хост — имя Photon-комнаты между матчами переиспользуется и id не годится)
    /// и PlayFabId соперника. Обе величины едут каналом VS-раскрытия командиров
    /// (RPC_SubmitCommander/RPC_RevealCommanders в PhotonRunHandler) — свой канал не нужен.
    ///
    /// Живёт в Service (виден Photon/Backend/Progression без новых зависимостей, как PlayerRating).
    /// В PvE/сюжете/туториале reveal не выполняется → IsSet=false → отчёт не шлётся.
    /// LocalPlayFabId кладёт BackendSession после логина (Photon не видит PlayFabService напрямую).
    /// При реконнекте восстанавливается из MatchSessionStore (ReconnectService) — reveal заново не придёт.
    /// </summary>
    public static class MatchIdentity
    {
        /// <summary>PlayFabId локального игрока (после логина; пусто до него).</summary>
        public static string LocalPlayFabId;

        public static string MatchId;
        public static string OpponentPlayFabId;

        public static bool IsSet => !string.IsNullOrEmpty(MatchId) && !string.IsNullOrEmpty(OpponentPlayFabId);

        public static void Set(string matchId, string opponentPlayFabId)
        {
            MatchId = matchId;
            OpponentPlayFabId = opponentPlayFabId;
            UnityEngine.Debug.Log($"[MatchIdentity] match='{matchId}' opp='{opponentPlayFabId}'");
        }

        /// <summary>Чистится после отправки отчёта (MatchReportService) и при выходе из боя (BattleState).</summary>
        public static void Clear()
        {
            MatchId = null;
            OpponentPlayFabId = null;
        }
    }
}
