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
        public int TargetPlayerCount = 2;
        public int SceneIndex = 2; 
        public string MatchName = "Ladder";
        public string GameVersion = "1.0";
        public int MaxRetries = 3;
        public float RetryDelay = 1f;
        public float SessionSearchTimeout = 5f;
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
                Debug.LogError($"[Matchmaking] Error: {ex.Message}");
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

        private string FindOrCreateSessionName(List<SessionInfo> sessions)
        {
            string baseSessionName = $"Match_{_config.MatchName}_{_config.GameVersion}_{_config.TargetPlayerCount}p";
            int roomNumber = 0;

            // Ищем комнату с свободными местами
            foreach (var session in sessions)
            {
                if (!session.Name.StartsWith(baseSessionName))
                    continue;

                // Проверяем есть ли свободные места
                if (session.PlayerCount < session.MaxPlayers && session.IsOpen && session.IsVisible)
                {
                    Debug.Log($"[Matchmaking] Found available session: {session.Name} ({session.PlayerCount}/{session.MaxPlayers})");
                    return session.Name;
                }

                // Запоминаем максимальный номер комнаты
                if (TryExtractRoomNumber(session.Name, baseSessionName, out int num))
                {
                    roomNumber = Mathf.Max(roomNumber, num);
                }
            }

            // Создаём новую комнату с инкрементированным номером
            string newSessionName = $"{baseSessionName}_{roomNumber + 1}";
            Debug.Log($"[Matchmaking] Creating new session: {newSessionName}");
            return newSessionName;
        }

        private bool TryExtractRoomNumber(string sessionName, string baseName, out int number)
        {
            number = 0;
            if (!sessionName.StartsWith(baseName + "_"))
                return false;

            string suffix = sessionName.Substring(baseName.Length + 1);
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
                    // Комната заполнилась пока мы подключались — ищем другую
                    Debug.Log($"[Matchmaking] Session {sessionName} is full, searching for another...");

                    var sessions = await FetchAvailableSessionsAsync();
                    sessionName = FindOrCreateSessionName(sessions);
                    retryCount++; // Не считаем это как полноценный retry
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Matchmaking] Join attempt {retryCount + 1} failed: {ex.Message}");
                    retryCount++;

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

        private async Task<MatchmakingResult> WaitForPlayersAsync(bool isHost, string sessionName)
        {
            var tcs = new TaskCompletionSource<MatchmakingResult>();

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
            }
        }

        public async Task CancelMatchmaking()
        {
            if (CurrentState == MatchmakingState.Idle)
                return;

            _isCancelled = true;
            _sessionListTcs?.TrySetCanceled();
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