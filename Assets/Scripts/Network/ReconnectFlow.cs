namespace Game.Core.Network
{
    // === helper (static bridge) ===
    /// <summary>
    /// СОСТОЯНИЕ РЕКОННЕКТА (паттерн NetHealth/ResyncBus: RPC-колбэки Fusion пишут, ECS/состояния читают
    /// в главном потоке). Живёт МЕЖДУ сценами и переживает пересоздание EcsWorld — потому статик, а не компонент.
    ///
    /// Две роли:
    ///   • ВЕРНУВШИЙСЯ (IsActive=true): помечает весь боевой бутстрап как «восстановление», а не новый матч —
    ///     InitPlayerSystem берёт идентичность из MatchSessionStore, колода/мулиган пропускаются (EcsRunHandler),
    ///     BattleState инициализируется сам (пайплайн хоста одноразовый), WorldResyncSystem просит снэпшот мира.
    ///   • ОСТАВШИЙСЯ (OpponentRestoring): пир вернулся и собирается — блокируем ввод, чтобы наши действия не
    ///     ушли в пустоту между снятием снэпшота и его применением (иначе они потерялись бы: Apply чистит ActionQueue).
    /// </summary>
    public static class ReconnectFlow
    {
        static readonly object _lock = new object();

        static bool _active;
        static bool _grantReceived, _grantAccepted;
        static bool _opponentRestoring;
        static MatchSessionStore.Record _saved;

        /// <summary>Мы возвращаемся в незавершённый матч (весь боевой бутстрап идёт по ветке восстановления).</summary>
        public static bool IsActive { get { lock (_lock) return _active; } }

        /// <summary>Сохранённая идентичность матча (валидна, пока IsActive).</summary>
        public static MatchSessionStore.Record Saved { get { lock (_lock) return _saved; } }

        public static void Begin(MatchSessionStore.Record rec)
        {
            lock (_lock)
            {
                _saved = rec;
                _active = true;
                _grantReceived = false;
                _grantAccepted = false;
            }
            UnityEngine.Debug.Log($"[Reconnect] старт восстановления: room='{rec.SessionName}' myId={rec.MyPlayerId} side={rec.MySide}");
        }

        // ── подтверждение от пира (пишет RPC-поток) ──────────────────────────────

        public static void NoteGrant(bool accepted)
        {
            lock (_lock) { _grantReceived = true; _grantAccepted = accepted; }
        }

        /// <summary>Пришёл ли ответ пира на запрос реконнекта (true в out — разрешил).</summary>
        public static bool TryTakeGrant(out bool accepted)
        {
            lock (_lock)
            {
                accepted = _grantAccepted;
                if (!_grantReceived) return false;
                _grantReceived = false;
                return true;
            }
        }

        // ── сторона ОСТАВШЕГОСЯ ──────────────────────────────────────────────────

        /// <summary>Пир заявил о возвращении: держим ввод заблокированным до отправки снэпшота.</summary>
        public static void NoteOpponentRestoring()
        {
            lock (_lock) _opponentRestoring = true;
        }

        public static bool OpponentRestoring { get { lock (_lock) return _opponentRestoring; } }

        public static void ClearOpponentRestoring()
        {
            lock (_lock) _opponentRestoring = false;
        }

        /// <summary>Восстановление закончено (успех или отказ) — дальше матч живёт обычной жизнью.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _active = false;
                _grantReceived = false;
                _grantAccepted = false;
                _opponentRestoring = false;
                _saved = default;
            }
        }
    }
}
