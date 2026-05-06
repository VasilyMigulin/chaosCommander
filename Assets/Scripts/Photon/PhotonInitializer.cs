using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine; 
using System.Threading.Tasks; 

namespace Game.Core.Photon
{
    public struct SessionParams
    {
        public GameMode Mode;
        public string RoomName;
        public int LobbySceneIndex;
        public string GameScenePath;
        public int TargetPlayerCount;
        public bool ProvideInput;
    }


    public class PhotonInitializer : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static PhotonInitializer Instance { get; private set; }

        private const int LOBBY_SCENE_INDEX = 2; // Индекса LobbyScene в Build Settings
        private NetworkObject runHandlerPrefab;

        [HideInInspector] public NetworkRunner Runner;

        // События
        public event Action<int, int> OnPlayersCountChanged;
        public event Action OnAllPlayersReady;
        public event Action<PlayerRef> OnPlayerJoinedEvent;
        public event Action<PlayerRef> OnPlayerLeftEvent;
        public event Action<List<SessionInfo>> OnSessionListReceived;

        private PhotonRunHandler currentHandler;
        private List<PlayerRef> connectedPlayers = new List<PlayerRef>();
        private SessionParams sessionParams;
        private MatchmakingService _matchmakingService;
        private NetworkRunner _browserRunner;

        public MatchmakingService Matchmaking => _matchmakingService;
        public int ConnectedPlayersCount => connectedPlayers.Count;
        public int TargetPlayersCount => sessionParams.TargetPlayerCount;

        /// <summary>
        /// PlayFab UserID локального игрока. Устанавливается перед входом в сессию.
        /// </summary>
        public string LocalPlayFabId { get; private set; }

        public static PhotonInitializer Initialize()
        {
            var instance = new GameObject("PhotonInitializer");

            var photonInitializer = instance.AddComponent<PhotonInitializer>();

            photonInitializer.SetPrefabPhotonHandler();

            //photonInitializer.LocalPlayFabId = ConfigModule.GetConfig<PlayfabConfig>().Service.PlayFabId;

            return photonInitializer;
        }

        private void SetPrefabPhotonHandler()
        {
            const string RESOURCES_PATH = "Network/PhotonRunHandler";

            // Загружаем префаб из Resources
            runHandlerPrefab = Resources.Load<NetworkObject>(RESOURCES_PATH);

            if (runHandlerPrefab != null)
            {
                Debug.Log($"[PhotonInitializer] Loaded PhotonRunHandler prefab from Resources/{RESOURCES_PATH}");
                return;
            }

            // Если префаб не найден в Resources
            Debug.LogError($"[PhotonInitializer] PhotonRunHandler prefab not found in Resources/{RESOURCES_PATH}!");

#if UNITY_EDITOR
            // В редакторе пытаемся создать префаб
            CreatePrefabInEditor();
#else
            Debug.LogError("[PhotonInitializer] Cannot create prefab in runtime! Please ensure prefab exists in Resources/Network/ folder.");
#endif
        }

#if UNITY_EDITOR
        private void CreatePrefabInEditor()
        {
            const string PREFAB_PATH = "Assets/Resources/Network/PhotonRunHandler.prefab";
            const string PREFAB_NAME = "PhotonRunHandler";

            Debug.Log($"[PhotonInitializer] Creating PhotonRunHandler prefab...");

            // Создаем временный GameObject
            var tempGO = new GameObject(PREFAB_NAME);

            // Добавляем необходимые компоненты
            var networkObject = tempGO.AddComponent<NetworkObject>();
            tempGO.AddComponent<PhotonRunHandler>();

            // Создаем директорию если её нет
            string directory = System.IO.Path.GetDirectoryName(PREFAB_PATH);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                UnityEditor.AssetDatabase.Refresh();
            }

            // Сохраняем как префаб
            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(tempGO, PREFAB_PATH);

            // Удаляем временный GameObject из сцены
            DestroyImmediate(tempGO);

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();

            // Загружаем созданный префаб через Resources
            runHandlerPrefab = Resources.Load<NetworkObject>("Network/PhotonRunHandler");

            if (runHandlerPrefab != null)
            {
                Debug.Log($"[PhotonInitializer] Successfully created and loaded PhotonRunHandler prefab");
            }
            else
            {
                Debug.LogError($"[PhotonInitializer] Failed to load created PhotonRunHandler prefab!");
            }
        }
#endif 
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _matchmakingService = new MatchmakingService(this);

            if (runHandlerPrefab == null)
            {
                Debug.LogError("[PhotonInitializer] RunHandler prefab is not assigned!");
            }
        }

        /// <summary>
        /// Запускает браузер сессий для получения списка доступных комнат
        /// </summary>
        public async Task StartSessionBrowser(string gameVersion)
        {
            await StopSessionBrowser();

            _browserRunner = gameObject.AddComponent<NetworkRunner>();
            _browserRunner.AddCallbacks(this);

            var result = await _browserRunner.JoinSessionLobby(SessionLobby.ClientServer);

            if (!result.Ok)
            {
                Debug.LogWarning($"[PhotonInitializer] Failed to join session lobby: {result.ShutdownReason}");
            }
        }

        /// <summary>
        /// Останавливает браузер сессий
        /// </summary>
        public async Task StopSessionBrowser()
        {
            if (_browserRunner == null) return;

            await _browserRunner.Shutdown(false);
            Destroy(_browserRunner);
            _browserRunner = null;

            // Ждём пока компонент полностью удалится
            while (gameObject.GetComponents<NetworkRunner>()
                              .Length > (Runner != null ? 1 : 0))
            {
                await Task.Delay(10);
            }
        }
        public async Task StartSession(SessionParams session)
        {
            await StopSessionBrowser();

            // Если Runner уже жив — сначала завершаем его
            if (Runner != null)
            {
                Debug.LogWarning("[PhotonInitializer] Runner already exists, ending previous session first.");
                await EndSession();
            }

            connectedPlayers.Clear();

            Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = session.ProvideInput;
            Runner.AddCallbacks(this);

            var sceneManager = Runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode = session.Mode,
                SessionName = session.RoomName,
                Scene = SceneRef.FromIndex(LOBBY_SCENE_INDEX), // Всегда загружаем LobbyScene 
                PlayerCount = session.TargetPlayerCount,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { "version", session.GameScenePath },
                    { "maxPlayers", session.TargetPlayerCount }
                }
            };

            sessionParams = session;

            var result = await Runner.StartGame(args);

            if (!result.Ok)
            {
                if (result.ShutdownReason == ShutdownReason.GameIsFull)
                {
                    throw new SessionFullException($"Session {session.RoomName} is full");
                }

                Debug.LogError($"[PhotonInitializer] Failed to start game: {result.ShutdownReason}");
                throw new Exception($"Failed to start: {result.ShutdownReason}");
            }

            Debug.Log($"[PhotonInitializer] Session '{session.RoomName}' started in Lobby. IsServer: {Runner.IsServer}");

            if (Runner.IsServer && currentHandler == null)
            {
                SpawnRunHandler();
            }
        }
        private void SpawnRunHandler()
        {
            var obj = Runner.Spawn(
            runHandlerPrefab,
            Vector3.zero,
            Quaternion.identity,
            inputAuthority: Runner.LocalPlayer,
            onBeforeSpawned: (runner, networkObject) =>
            {
                currentHandler = networkObject.GetComponent<PhotonRunHandler>();
                var data = new NetworkSessionData();
                data.Seed = UnityEngine.Random.Range(0, 1000);
                data.ScenePath = new NetworkString<_128>(sessionParams.GameScenePath);
                data.TargetPlayerCount = sessionParams.TargetPlayerCount;
                currentHandler.SessionData = data;
            },
            flags: NetworkSpawnFlags.DontDestroyOnLoad
            );

            if (obj == null)
            {
                Debug.LogError("[PhotonInitializer] Failed to spawn RunHandler.");
            }
        }
        public async Task EndSession()
        {
            if (currentHandler != null)
            {
                if (currentHandler.Object != null && currentHandler.Object.IsValid && Runner != null && Runner.IsServer)
                    Runner.Despawn(currentHandler.Object);

                currentHandler = null;
            }

            if (Runner != null)
            {
                await Runner.Shutdown(false);
                Destroy(Runner);

                while (gameObject.GetComponents<NetworkRunner>()
                                  .Length > (_browserRunner != null ? 1 : 0))
                {
                    await Task.Delay(10);
                }

                Runner = null;
            }

            connectedPlayers.Clear();
        }

        #region INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner == _browserRunner)
                return;

            if (!connectedPlayers.Contains(player))
                connectedPlayers.Add(player);

            Debug.Log($"[PhotonInitializer] Player joined: {player}, total: {connectedPlayers.Count}/{sessionParams.TargetPlayerCount}");

            OnPlayerJoinedEvent?.Invoke(player);
            OnPlayersCountChanged?.Invoke(connectedPlayers.Count, sessionParams.TargetPlayerCount);

            if (connectedPlayers.Count >= sessionParams.TargetPlayerCount)
            {
                Debug.Log("[PhotonInitializer] All players connected!");
                OnAllPlayersReady?.Invoke();
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner == _browserRunner)
                return;

            if (connectedPlayers.Contains(player))
                connectedPlayers.Remove(player);

            Debug.Log($"[PhotonInitializer] Player left: {player}, total: {connectedPlayers.Count}");

            OnPlayerLeftEvent?.Invoke(player);
            OnPlayersCountChanged?.Invoke(connectedPlayers.Count, sessionParams.TargetPlayerCount);
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            Debug.Log($"[PhotonInitializer] Session list updated: {sessionList.Count} sessions found");

            foreach (var session in sessionList)
            {
                Debug.Log($"  - {session.Name}: {session.PlayerCount}/{session.MaxPlayers} (Open: {session.IsOpen})");
            }

            OnSessionListReceived?.Invoke(sessionList);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[PhotonInitializer] Shutdown: {shutdownReason}");

            if (runner != _browserRunner)
                connectedPlayers.Clear();
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            /*var local = NetworkCharacterView.LocalInstance;

            if (local != null)
            {
                var snapshot = NetworkInputCollector.Consume();
                var data = new NetworkInputData
                {
                    Position = local.transform.position,
                    Rotation = local.transform.rotation,
                    AimRotation = local.UpperBodyTransform != null ? local.UpperBodyTransform.rotation : local.transform.rotation,
                    ButtonsHeld = snapshot.Held,
                    ButtonsDown = snapshot.Down,
                    ButtonsUp = snapshot.Up
                };
                input.Set(data);
            }*/
        }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.Log($"[PhotonInitializer] Disconnected: {reason}");
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Debug.Log("[PhotonInitializer] Connected to server");
        }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        #endregion
    }
}