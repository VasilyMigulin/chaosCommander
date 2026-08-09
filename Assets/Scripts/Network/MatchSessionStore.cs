using System;
using UnityEngine;

namespace Game.Core.Network
{
    // === helper (persist) ===
    /// <summary>
    /// ПЕРСИСТ АКТИВНОГО МАТЧА (фаза 2 сетевой устойчивости — реконнект после ПЕРЕЗАПУСКА приложения).
    ///
    /// Мир живёт только в памяти (EcsWorld пересоздаётся в EcsRunHandler), поэтому вернувшийся клиент
    /// собирает бой из ДВУХ источников: снэпшот мира от ОСТАВШЕГОСЯ пира (борд/зоны/HP/счётчики — см.
    /// WorldResyncSystem) плюс эта запись — то, чего пир про нас знать НЕ может:
    ///   • имя комнаты (иначе в сессию не вернуться: матчмейкинг ищет по MMR и отсеивает полные);
    ///   • наша игровая идентичность (PlayerId/Side) — Fusion при повторном входе выдаёт ДРУГОЙ слот, а от
    ///     PlayerId зависят сторона доски, размер стартовой руки и ПРЕФИКС сетевых ключей (NetKey);
    ///   • наши ресурсы/личный счётчик ходов — пассив не зеркалит чужой доход, в снэпшоте пира они протухшие.
    ///
    /// Пишется на старте боя (идентичность) и обновляется на границах ходов (прогресс); чистится по концу
    /// матча и выходу в меню. PlayerPrefs Unity сам сбрасывает на диск при сворачивании/выходе.
    /// </summary>
    public static class MatchSessionStore
    {
        const string KeySession  = "mp.match.session";
        const string KeySavedAt  = "mp.match.savedAt";    // UtcNow.Ticks строкой (long в PlayerPrefs нет)
        const string KeyMyId     = "mp.match.myId";
        const string KeyMySide   = "mp.match.mySide";
        const string KeyOppId    = "mp.match.oppId";
        const string KeyMatchId  = "mp.match.matchId";     // GUID матча (рейтинг: отчёт после реконнекта)
        const string KeyOppPf    = "mp.match.oppPlayFab";  // PlayFabId соперника (туда же)
        const string KeyGold     = "mp.match.gold";
        const string KeyGoldMax  = "mp.match.goldMax";
        const string KeyMana     = "mp.match.mana";
        const string KeyManaMax  = "mp.match.manaMax";
        const string KeyPersonal = "mp.match.personal";
        const string KeyTurn     = "mp.match.turn";

        /// <summary>
        /// Предел свежести записи. Щедрый намеренно: метка обновляется лишь на границах ходов (ход тянется
        /// до TurnConfig.TurnDuration), а окончательный вердикт всё равно за пиром — он отклонит реконнект,
        /// если матча уже нет (см. PhotonRunHandler.RPC_RequestReconnect). Здесь только отсечка совсем старья.
        /// </summary>
        public const double MaxAgeSeconds = 300;

        public struct Record
        {
            public string SessionName;
            public int MyPlayerId, MySide, OpponentPlayerId;
            public int Gold, GoldMax, Mana, ManaMax, PersonalTurn, TurnNumber;
            public string MatchId, OpponentPlayFabId;   // рейтинг: могут быть пустыми (соперник без логина)
        }

        /// <summary>Есть ли свежая запись о незавершённом матче (быстрая проверка без разбора).</summary>
        public static bool HasActive => TryLoad(out _);

        /// <summary>Старт боя: имя комнаты + наша идентичность. Ресурсы придут первым SaveProgress.
        /// matchId/opponentPlayFabId — для отчёта о рейтинге после реконнекта (могут быть пустыми).</summary>
        public static void SaveIdentity(string sessionName, int myPlayerId, int mySide, int opponentPlayerId,
            string matchId = null, string opponentPlayFabId = null)
        {
            if (string.IsNullOrEmpty(sessionName))
            {
                Debug.LogWarning("[MatchStore] пустое имя комнаты — реконнект будет невозможен");
                return;
            }
            PlayerPrefs.SetString(KeySession, sessionName);
            PlayerPrefs.SetInt(KeyMyId, myPlayerId);
            PlayerPrefs.SetInt(KeyMySide, mySide);
            PlayerPrefs.SetInt(KeyOppId, opponentPlayerId);
            PlayerPrefs.SetString(KeyMatchId, matchId ?? string.Empty);
            PlayerPrefs.SetString(KeyOppPf, opponentPlayFabId ?? string.Empty);
            Stamp();
            Debug.Log($"[MatchStore] матч сохранён: room='{sessionName}' myId={myPlayerId} side={mySide} oppId={opponentPlayerId}");
        }

        /// <summary>Граница хода: ресурсы/счётчики + обновление метки свежести.</summary>
        public static void SaveProgress(int gold, int goldMax, int mana, int manaMax, int personalTurn, int turnNumber)
        {
            if (!PlayerPrefs.HasKey(KeySession)) return;   // идентичности нет — прогресс некуда цеплять
            PlayerPrefs.SetInt(KeyGold, gold);
            PlayerPrefs.SetInt(KeyGoldMax, goldMax);
            PlayerPrefs.SetInt(KeyMana, mana);
            PlayerPrefs.SetInt(KeyManaMax, manaMax);
            PlayerPrefs.SetInt(KeyPersonal, personalTurn);
            PlayerPrefs.SetInt(KeyTurn, turnNumber);
            Stamp();
        }

        static void Stamp()
        {
            PlayerPrefs.SetString(KeySavedAt, DateTime.UtcNow.Ticks.ToString());
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out Record rec)
        {
            rec = default;
            string session = PlayerPrefs.GetString(KeySession, null);
            if (string.IsNullOrEmpty(session)) return false;

            if (!long.TryParse(PlayerPrefs.GetString(KeySavedAt, "0"), out long ticks) || ticks <= 0) return false;
            double age = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ticks).TotalSeconds;
            if (age < 0 || age > MaxAgeSeconds)
            {
                Debug.Log($"[MatchStore] запись протухла ({age:F0}с > {MaxAgeSeconds}с) — чищу");
                Clear();
                return false;
            }

            rec = new Record
            {
                SessionName      = session,
                MyPlayerId       = PlayerPrefs.GetInt(KeyMyId, -1),
                MySide           = PlayerPrefs.GetInt(KeyMySide, 1),
                OpponentPlayerId = PlayerPrefs.GetInt(KeyOppId, -1),
                Gold             = PlayerPrefs.GetInt(KeyGold, 0),
                GoldMax          = PlayerPrefs.GetInt(KeyGoldMax, 0),
                Mana             = PlayerPrefs.GetInt(KeyMana, 0),
                ManaMax          = PlayerPrefs.GetInt(KeyManaMax, 0),
                PersonalTurn     = PlayerPrefs.GetInt(KeyPersonal, 0),
                TurnNumber       = PlayerPrefs.GetInt(KeyTurn, 1),
                MatchId          = PlayerPrefs.GetString(KeyMatchId, string.Empty),
                OpponentPlayFabId = PlayerPrefs.GetString(KeyOppPf, string.Empty),
            };
            return rec.MyPlayerId >= 0 && rec.OpponentPlayerId >= 0;
        }

        public static void Clear()
        {
            foreach (var k in new[] { KeySession, KeySavedAt, KeyMyId, KeyMySide, KeyOppId,
                                      KeyMatchId, KeyOppPf,
                                      KeyGold, KeyGoldMax, KeyMana, KeyManaMax, KeyPersonal, KeyTurn })
                PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }
}
