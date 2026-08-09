using Fusion;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Core.Photon
{
    [Serializable]
    public class MatchmakingConfig
    {
#if UNITY_EDITOR
        public int TargetPlayerCount = 2;
#else
        public int TargetPlayerCount = 2;
#endif
        public int SceneIndex = 2;
        public string MatchName = "Ladder";
        public string GameVersion = "1.0";
        public int MaxRetries = 3;
        public float RetryDelay = 1f;
        public float SessionSearchTimeout = 5f;

        // ── Подбор по MMR ───────────────────────────────────────────────────────
        /// <summary>MMR ищущего. 0 → подставится PlayerRating.Mmr при старте поиска.</summary>
        public int Mmr = 0;

        /// <summary>Допуск: соперник «свой», если |его MMR − мой| ≤ этого значения.</summary>
        public int MmrTolerance = 150;

        /// <summary>
        /// true → к сопернику вне допуска НЕ подсаживаемся (создаём свою комнату и ждём «своих»).
        /// false (по умолчанию, MVP/тест) → если в допуске никого нет, садимся к БЛИЖАЙШЕМУ по MMR:
        /// при пустом онлайне матч всё равно состоится, как и до появления рейтинга.
        /// Включать, когда онлайн вырастет — иначе игроки будут висеть в поиске.
        /// </summary>
        public bool StrictMmr = false;
    }

    public enum MatchmakingState
    {
        Idle,
        SearchingSessions,
        Joining,
        WaitingForPlayers,
        Ready,
        Failed,
        Cancelled
    }

    public class MatchmakingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsHost { get; set; }
        public int PlayerCount { get; set; }
        public string SessionName { get; set; }
    }

    public class MatchmakingService
    {
        public event Action<MatchmakingState> OnStateChanged;
        public event Action<int, int> OnPlayerCountChanged;
        public event Action<MatchmakingResult> OnMatchFound;
        public event Action<string> OnMatchmakingFailed;

        public MatchmakingState CurrentState { get; private set; } = MatchmakingState.Idle;

        private readonly PhotonInitializer _photonInitializer;
        private MatchmakingConfig _config;
        private bool _isCancelled;
        private bool _isSuccess;
        private List<SessionInfo> _availableSessions = new List<SessionInfo>();
        private TaskCompletionSource<List<SessionInfo>> _sessionListTcs;

        public MatchmakingService(PhotonInitializer photonInitializer)
        {
            _photonInitializer = photonInitializer;
        }

        public async Task<MatchmakingResult> FindMatchAsync(MatchmakingConfig config = null)
        {
            if (CurrentState != MatchmakingState.Idle)
            {
                return new MatchmakingResult
                {
                    Success = false,
                    ErrorMessage = "Matchmaking already in progress"
                };
            }

            _config = config ?? new MatchmakingConfig();
            if (_config.Mmr <= 0) _config.Mmr = Service.PlayerRating.Mmr;
            _isCancelled = false;

            SetState(MatchmakingState.SearchingSessions);

            try
            {
                // Шаг 1: Получаем список доступных сессий
                var sessions = await FetchAvailableSessionsAsync();

                if (_isCancelled)
                    return CancelledResult();

                // Шаг 2: Ищем подходящую сессию или создаём новую
                string targetSession = FindOrCreateSessionName(sessions);

                Debug.Log($"[Matchmaking] Target session: {targetSession}");

                // Шаг 3: Подключаемся
                return await JoinSessionAsync(targetSession);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Matchmaking] Error: {ex.Message}\n{ex.StackTrace}");

                // Чистим возможный недо-инициализированный раннер, иначе следующий поиск снова упадёт.
                await SafeEndSession();

                SetState(MatchmakingState.Failed);
                OnMatchmakingFailed?.Invoke(ex.Message);
                SetState(MatchmakingState.Idle);
                return new MatchmakingResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>Гарантированно завершает сессию, не пробрасывая исключений.</summary>
        private async Task SafeEndSession()
        {
            try
            {
                if (_photonInitializer != null)
                    await _photonInitializer.EndSession();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matchmaking] SafeEndSession failed: {e.Message}");
            }
        }

        private async Task<List<SessionInfo>> FetchAvailableSessionsAsync()
        {
            _sessionListTcs = new TaskCompletionSource<List<SessionInfo>>();

            // Подписываемся на получение списка сессий
            _photonInitializer.OnSessionListReceived += HandleSessionListReceived;

            try
            {
                // Запускаем поиск сессий в режиме клиента без подключения
                await _photonInitializer.StartSessionBrowser(_config.GameVersion);

                // Ждём получения списка или таймаут
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_config.SessionSearchTimeout));
                var completedTask = await Task.WhenAny(_sessionListTcs.Task, timeoutTask);

                await _photonInitializer.StopSessionBrowser();

                if (completedTask == timeoutTask)
                {
                    Debug.Log("[Matchmaking] Session search timeout, will create new session");
                    return new List<SessionInfo>();
                }

                return await _sessionListTcs.Task;
            }
            finally
            {
                _photonInitializer.OnSessionListReceived -= HandleSessionListReceived;
            }
        }

        private void HandleSessionListReceived(List<SessionInfo> sessions)
        {
            _sessionListTcs?.TrySetResult(sessions);
        }

        /// <summary>
        /// Выбор комнаты. Fusion умеет фильтровать сессии только по ТОЧНОМУ совпадению свойств
        /// (SQL-лобби из PUN тут нет), поэтому диапазон MMR считаем сами по списку лобби — благо
        /// список мы и так тянем. MMR хоста едет в ИМЕНИ комнаты («…_m1480_3») — не нужны
        /// SessionProperties и правки StartGameArgs.
        ///
        /// Берём НЕ первую попавшуюся, а ближайшую по MMR среди открытых. Вне допуска: StrictMmr=false
        /// (MVP) — всё равно садимся к ближайшему; StrictMmr=true — создаём свою комнату.
        /// </summary>
        private string FindOrCreateSessionName(List<SessionInfo> sessions)
        {
            string baseSessionName = $"Match_{_config.MatchName}_{_config.GameVersion}_{_config.TargetPlayerCount}p";
            int roomNumber = 0;

            SessionInfo best = null;
            int bestDiff = int.MaxValue;

            foreach (var session in sessions ?? new List<SessionInfo>())
            {
                if (!session.Name.StartsWith(baseSessionName))
                    continue;

                if (TryParseSessionName(session.Name, baseSessionName, out int hostMmr, out int num))
                    roomNumber = Mathf.Max(roomNumber, num);   // номер комнаты держим уникальным

                if (session.PlayerCount >= session.MaxPlayers || !session.IsOpen || !session.IsVisible)
                    continue;

                // Комната без MMR в имени (старый формат) — «бесконечно далёкая»: годится только как
                // фолбэк, когда ближе никого нет и StrictMmr выключен.
                int diff = hostMmr > 0 ? Mathf.Abs(hostMmr - _config.Mmr) : int.MaxValue - 1;
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = session;
                }
            }

            if (best != null)
            {
                bool inRange = bestDiff <= _config.MmrTolerance;
                if (inRange || !_config.StrictMmr)
                {
                    Debug.Log($"[Matchmaking] Found available session: {best.Name} ({best.PlayerCount}/{best.MaxPlayers}), " +
                              $"MMR diff {bestDiff}" + (inRange ? "" : $" — вне допуска ±{_config.MmrTolerance}, беру ближайшего (StrictMmr=false)"));
                    return best.Name;
                }

                Debug.Log($"[Matchmaking] Ближайшая комната вне допуска (diff {bestDiff} > {_config.MmrTolerance}) — создаю свою.");
            }

            // Создаём новую комнату: свой MMR + инкрементированный номер
            string newSessionName = $"{baseSessionName}_m{_config.Mmr}_{roomNumber + 1}";
            Debug.Log($"[Matchmaking] Creating new session: {newSessionName} (MMR {_config.Mmr})");
            return newSessionName;
        }

        /// <summary>
        /// Разбирает имя комнаты «{base}_m{mmr}_{num}». Комнаты старого формата «{base}_{num}»
        /// тоже понимаются — у них mmr = 0 (не подсчитан).
        /// </summary>
        private bool TryParseSessionName(string sessionName, string baseName, out int mmr, out int number)
        {
            mmr = 0;
            number = 0;
            if (!sessionName.StartsWith(baseName + "_"))
                return false;

            string suffix = sessionName.Substring(baseName.Length + 1);   // «m1480_3» или «3»

            if (suffix.Length > 1 && suffix[0] == 'm')
            {
                int sep = suffix.IndexOf('_');
                if (sep < 2 || !int.TryParse(suffix.Substring(1, sep - 1), out mmr))
                {
                    mmr = 0;
                    return false;
                }
                return int.TryParse(suffix.Substring(sep + 1), out number);
            }

            return int.TryParse(suffix, out number);
        }

        private async Task<MatchmakingResult> JoinSessionAsync(string sessionName)
        {
            SetState(MatchmakingState.Joining);

            _isSuccess = false;
            int retryCount = 0;

            while (retryCount < _config.MaxRetries && !_isCancelled && !_isSuccess)
            {
                var sessionParams = new SessionParams
                {
                    Mode = GameMode.AutoHostOrClient,
                    RoomName = sessionName,
                    LobbySceneIndex = _config.SceneIndex, 
                    TargetPlayerCount = _config.TargetPlayerCount,
                    ProvideInput = true
                };

                try
                {
                    await _photonInitializer.StartSession(sessionParams);

                    if (_isCancelled)
                    {
                        await _photonInitializer.EndSession();
                        return CancelledResult();
                    }

                    // Проверяем успешность подключения
                    if (_photonInitializer.Runner == null || !_photonInitializer.Runner.IsRunning)
                    {
                        throw new Exception("Runner failed to start");
                    }

                    SetState(MatchmakingState.WaitingForPlayers);

                    bool isHost = _photonInitializer.Runner.IsServer;
                    Debug.Log($"[Matchmaking] Connected to {sessionName} as {(isHost ? "Host" : "Client")}");
                    _isSuccess = true;
                    return await WaitForPlayersAsync(isHost, sessionName);
                }
                catch (SessionFullException)
                {
                    // Комната заполнилась пока мы подключались — чистим раннер и ищем другую
                    Debug.Log($"[Matchmaking] Session {sessionName} is full, searching for another...");

                    await SafeEndSession();

                    var sessions = await FetchAvailableSessionsAsync();
                    sessionName = FindOrCreateSessionName(sessions);
                    retryCount++; // Не считаем это как полноценный retry
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Matchmaking] Join attempt {retryCount + 1} failed: {ex.Message}");
                    retryCount++;

                    // Сносим «сломанный» раннер, чтобы следующая попытка стартовала с чистого состояния.
                    await SafeEndSession();

                    if (retryCount < _config.MaxRetries)
                    {
                        await Task.Delay((int)(_config.RetryDelay * 1000));
                    }
                }
            }

            SetState(MatchmakingState.Failed);
            SetState(MatchmakingState.Idle);
            return new MatchmakingResult
            {
                Success = false,
                ErrorMessage = "Failed to join match after all retries"
            };
        }

        // Живое ожидание оппонента — CancelMatchmaking будит его немедленно (иначе таск поиска висел
        // до 5-минутного таймаута после отмены, держа sessionTask/подписки).
        private TaskCompletionSource<MatchmakingResult> _waitPlayersTcs;

        private async Task<MatchmakingResult> WaitForPlayersAsync(bool isHost, string sessionName)
        {
            var tcs = new TaskCompletionSource<MatchmakingResult>();
            _waitPlayersTcs = tcs;

            void OnPlayersReady(int currentCount, int targetCount)
            {
                OnPlayerCountChanged?.Invoke(currentCount, targetCount);

                if (currentCount >= targetCount)
                {
                    tcs.TrySetResult(new MatchmakingResult
                    {
                        Success = true,
                        IsHost = isHost,
                        PlayerCount = currentCount,
                        SessionName = sessionName
                    });
                }
            }

            _photonInitializer.OnPlayersCountChanged += OnPlayersReady;

            // Сразу проверяем текущее количество
            OnPlayersReady(_photonInitializer.ConnectedPlayersCount, _config.TargetPlayerCount);

            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (_isCancelled)
                    return CancelledResult();

                if (completedTask == timeoutTask)
                {
                    SetState(MatchmakingState.Failed);
                    SetState(MatchmakingState.Idle);
                    return new MatchmakingResult
                    {
                        Success = false,
                        ErrorMessage = "Timeout waiting for players"
                    };
                }

                var result = await tcs.Task;
                SetState(MatchmakingState.Ready);
                OnMatchFound?.Invoke(result);
                SetState(MatchmakingState.Idle);
                return result;
            }
            finally
            {
                _photonInitializer.OnPlayersCountChanged -= OnPlayersReady;
                if (_waitPlayersTcs == tcs) _waitPlayersTcs = null;
            }
        }

        public async Task CancelMatchmaking()
        {
            if (CurrentState == MatchmakingState.Idle)
                return;

            _isCancelled = true;
            _sessionListTcs?.TrySetCanceled();
            _waitPlayersTcs?.TrySetResult(CancelledResult());   // разбудить ожидание оппонента сразу (_isCancelled вернёт CancelledResult)
            SetState(MatchmakingState.Cancelled);

            await _photonInitializer.EndSession();
            SetState(MatchmakingState.Idle);
        }

        private MatchmakingResult CancelledResult() => new MatchmakingResult
        {
            Success = false,
            ErrorMessage = "Matchmaking cancelled"
        };

        private void SetState(MatchmakingState newState)
        {
            if (CurrentState != newState)
            {
                CurrentState = newState;
                OnStateChanged?.Invoke(newState);
                Debug.Log($"[Matchmaking] State: {newState}");
            }
        }
    }

    /// <summary>
    /// Исключение когда сессия заполнена
    /// </summary>
    public class SessionFullException : Exception
    {
        public SessionFullException(string message) : base(message) { }
    }
}