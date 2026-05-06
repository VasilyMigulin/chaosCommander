using Fusion;
using Game.Core.Shared.Interface;
using Game.Core.Events;
using Leopotam.EcsLite;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core.Photon
{
    public enum SessionState
    {
        None,
        WaitingInLobby,
        LoadingGameScene,
        InitializingGameWorld,
        InitializingGameState,
        InitializingPlayerCharacters,
        GameStarted
    }

    public enum PlayerLoadingStage
    {
        None,
        Connected,
        SceneLoaded,
        WorldInitialized,
        StateInitialized,
        CharacterSpawned
    }

    public struct NetworkPlayerInfo
    {
        public PlayerLoadingStage LoadingStage;
        public string PlayFabUserId;
        public string CharacterDataJson;
    }

    /// <summary>
    /// Defines a single phase in the session loading pipeline.
    /// To add new loading phases, create a new SessionPhaseRule with the desired
    /// SessionState, required PlayerLoadingStage, and optional enter action,
    /// then insert it into the pipeline in BuildPhasePipeline().
    /// </summary>
    public class SessionPhaseRule
    {
        public SessionState State { get; }
        public PlayerLoadingStage RequiredPlayerStage { get; }

        private readonly Action _onEnter;

        /// <param name="state">The SessionState this phase represents.</param>
        /// <param name="requiredStage">All players must reach this stage before the pipeline advances. 
        /// Use PlayerLoadingStage.None for terminal phases that never auto-advance.</param>
        /// <param name="onEnter">Optional action invoked on the server when entering this phase (e.g. send RPCs to clients).</param>
        public SessionPhaseRule(SessionState state, PlayerLoadingStage requiredStage, Action onEnter = null)
        {
            State = state;
            RequiredPlayerStage = requiredStage;
            _onEnter = onEnter;
        }

        public void Enter() => _onEnter?.Invoke();

        public bool IsCompleted(Func<PlayerLoadingStage, bool> areAllPlayersAtStage)
        {
            return RequiredPlayerStage != PlayerLoadingStage.None
                && areAllPlayersAtStage(RequiredPlayerStage);
        }
    }

    public partial class PhotonRunHandler : NetworkBehaviour
    {
        private IGameStateContext _state; 
        [Networked] public NetworkSessionData SessionData { get; set; }
        [Networked] public SessionState CurrentState { get; set; }
        [Networked] public int PlayersReady { get; set; }
        [Networked] public int PlayersSceneLoaded { get; set; }

        public float DeltaTime => runner.DeltaTime;
        public bool IsServer => runner.IsServer;

        // Событие для уведомления хоста об изменении прогресса загрузки игрока
        public event System.Action<PlayerRef, PlayerLoadingStage> OnPlayerProgressChanged;

        private NetworkRunner runner;
        private bool _isLocalSceneLoaded;

        // Серверный словарь для отслеживания прогресса загрузки каждого игрока
        private readonly Dictionary<PlayerRef, NetworkPlayerInfo> _playerProgress = new Dictionary<PlayerRef, NetworkPlayerInfo>();
        public IReadOnlyDictionary<PlayerRef, NetworkPlayerInfo> PlayerProgress => _playerProgress;

        // Pipeline загрузки сессии
        private readonly List<SessionPhaseRule> _phasePipeline = new List<SessionPhaseRule>();
        private int _currentPhaseIndex = -1;

        public override void Spawned()
        {
            base.Spawned();
            runner = Runner;

            if (IsServer)
            {
                CurrentState = SessionState.WaitingInLobby;
                _playerProgress.Clear();
                Debug.Log("[PhotonRunHandler] Server: Waiting in lobby");
            }

            // Подписываемся на события PhotonInitializer
            if (PhotonInitializer.Instance != null)
            {
                PhotonInitializer.Instance.OnAllPlayersReady += OnAllPlayersConnected; 
            }

            Debug.Log("[PhotonInitializer] PhotonRunHandler spawned successfully");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            base.Despawned(runner, hasState);

            if (PhotonInitializer.Instance != null)
            {
                PhotonInitializer.Instance.OnAllPlayersReady -= OnAllPlayersConnected;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!IsServer)
                return;

            if (_currentPhaseIndex < 0 || _currentPhaseIndex >= _phasePipeline.Count)
                return;

            var currentPhase = _phasePipeline[_currentPhaseIndex];
            if (currentPhase.IsCompleted(AreAllPlayersAtStage))
            {
                AdvancePhase();
            }
        }

        public override void Render()
        {
            base.Render();
        }

        private void OnAllPlayersConnected()
        {
            if (!IsServer)
                return;

            var oldProgress = new Dictionary<PlayerRef, NetworkPlayerInfo>(_playerProgress);
            _playerProgress.Clear();
            foreach (var player in runner.ActivePlayers)
            {
                string existingPlayFabId = oldProgress.ContainsKey(player) ? oldProgress[player].PlayFabUserId : null;
                _playerProgress[player] = new NetworkPlayerInfo
                {
                    LoadingStage = PlayerLoadingStage.Connected,
                    PlayFabUserId = existingPlayFabId
                };
            }

            Debug.Log($"[PhotonRunHandler] All {_playerProgress.Count} players connected, starting loading pipeline");
            PlayersSceneLoaded = 0;
            PlayersReady = 0;

            BuildPhasePipeline();
            StartPipeline();
        }

        /// <summary>
        /// Конфигурация pipeline загрузки. Чтобы добавить новый этап,
        /// вставьте новый SessionPhaseRule в нужную позицию списка.
        /// </summary>
        private void BuildPhasePipeline()
        {
            _phasePipeline.Clear();

            _phasePipeline.Add(new SessionPhaseRule(
                SessionState.LoadingGameScene,
                PlayerLoadingStage.SceneLoaded,
                () => RPC_LoadGameScene()));

            _phasePipeline.Add(new SessionPhaseRule(
                SessionState.InitializingGameWorld,
                PlayerLoadingStage.WorldInitialized,
                () => RPC_InitializeGame()));

            _phasePipeline.Add(new SessionPhaseRule(
                SessionState.InitializingGameState,
                PlayerLoadingStage.StateInitialized));

            _phasePipeline.Add(new SessionPhaseRule(
                SessionState.InitializingPlayerCharacters,
                PlayerLoadingStage.CharacterSpawned,
                () => SpawnCharForPlayers()));

            _phasePipeline.Add(new SessionPhaseRule(
                SessionState.GameStarted,
                PlayerLoadingStage.None,
                () => RPC_StartGame()));
        }

        private void StartPipeline()
        {
            _currentPhaseIndex = -1;
            AdvancePhase();
        }

        private void AdvancePhase()
        {
            _currentPhaseIndex++;
            if (_currentPhaseIndex >= _phasePipeline.Count)
                return;

            var phase = _phasePipeline[_currentPhaseIndex];
            CurrentState = phase.State;
            Debug.Log($"[PhotonRunHandler] Phase transition -> {phase.State}");
            phase.Enter();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_LoadGameScene()
        {
            Debug.Log($"[PhotonRunHandler] Loading game scene: {SessionData.ScenePath.Value}");
            LoadGameScene();
        }

        private void LoadGameScene()
        {
            if (_isLocalSceneLoaded)
                return;

            Debug.Log("[PhotonRunHandler] Loading game scene by index 3");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(3, LoadSceneMode.Additive);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLocalSceneLoaded = true;
            Debug.Log($"[PhotonRunHandler] Scene '{scene.name}' loaded");
            RPC_NotifySceneLoaded();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_NotifySceneLoaded(RpcInfo info = default)
        {
            if (!IsServer)
                return;

            if (_state == null)
            {
                var monoBehaviours = FindObjectsByType<MonoBehaviour>(sortMode: FindObjectsSortMode.None);

                foreach (var mono in monoBehaviours)
                {
                    if (mono.TryGetComponent<IGameStateContext>(out var state))
                    {
                        _state = state;
                    }
                }
            }

            if (_state == null)
            {
                Debug.LogError("Not found Game state context!");
                return;
            }

            PlayerRef player = info.Source;

            if (UpdatePlayerStage(player, PlayerLoadingStage.SceneLoaded))
            {
                PlayersSceneLoaded = GetPlayersAtStageCount(PlayerLoadingStage.SceneLoaded);
                Debug.Log($"[PhotonRunHandler] Player {player} scene loaded: {PlayersSceneLoaded}/{_playerProgress.Count}");
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_InitializeGame()
        {
            Debug.Log("[PhotonRunHandler] Initializing game world");
            InitializeGameWorld();
            // Уведомляем сервер о завершении инициализации мира
            RPC_NotifyWorldLoaded();
        }

        private void InitializeGameWorld()
        {
            // TODO: Генерация карты
            Debug.Log("[PhotonRunHandler] Generating map...");
            Debug.Log("[PhotonRunHandler] Game world initialized locally");
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_NotifyWorldLoaded(RpcInfo info = default)
        {
            if (!IsServer)
                return;

            PlayerRef player = info.Source;
            if (UpdatePlayerStage(player, PlayerLoadingStage.WorldInitialized))
            {
                PlayersReady = GetPlayersAtStageCount(PlayerLoadingStage.WorldInitialized);
                Debug.Log($"[PhotonRunHandler] Player {player} world initialized: {PlayersReady}/{_playerProgress.Count}");
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_NotifyStateReady(RpcInfo info = default)
        {
            if (!IsServer)
                return;

            PlayerRef player = info.Source;

            if (UpdatePlayerStage(player, PlayerLoadingStage.StateInitialized))
            {
                PlayersReady = GetPlayersAtStageCount(PlayerLoadingStage.StateInitialized);
                Debug.Log($"[PhotonRunHandler] Player {player} states initialized: {PlayersReady}/{_playerProgress.Count}");
            }
        }
         

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StartGame()
        {
            Debug.Log("[PhotonRunHandler] Game started!");
            GameEventBus.Publish(new Game.Core.Events.AllMulligansCompletedEvent());
        }

        /// <summary>
        /// <summary>
        /// Локальный клиент отправляет данные своей колоды оппоненту.
        /// Вызывается после завершения мулигана.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SyncDeckSnapshot(NetworkDeckSnapshotData snapshot, int fromPlayerId, RpcInfo info = default)
        {
            var runner = Runner;
            if (runner != null && info.Source == runner.LocalPlayer)
                return;

            if (_state == null)
                return;

            var world      = _state.World;
            var playerPool = world.GetPool<Game.Core.Ecs.Components.PlayerComponent>();
            var syncPool   = world.GetPool<Game.Core.Ecs.Components.OpponentDeckSyncComponent>();
            var filter     = world.Filter<Game.Core.Ecs.Components.PlayerComponent>().End();

            int opponentEntity = -1;
            foreach (var e in filter)
            {
                ref var p = ref playerPool.Get(e);
                if (p.PlayerId == fromPlayerId) { opponentEntity = e; break; }
            }

            if (opponentEntity == -1)
            {
                opponentEntity = world.NewEntity();
                ref var p = ref playerPool.Add(opponentEntity);
                p.PlayerId      = fromPlayerId;
                p.IsLocalPlayer = false;

                var sidePool = world.GetPool<Game.Core.Ecs.Components.PlayerSideComponent>();
                ref var side = ref sidePool.Add(opponentEntity);
                side.Side    = fromPlayerId;

                world.GetPool<Game.Core.Ecs.Components.DeckComponent>().Add(opponentEntity);
                world.GetPool<Game.Core.Ecs.Components.HandComponent>().Add(opponentEntity);

                _state.AddEntity(opponentEntity, $"player_{fromPlayerId}");
            }

            var expansionIds = new string[snapshot.DeckCount];
            var cardIds      = new int[snapshot.DeckCount];
            var netKeys      = new string[snapshot.DeckCount];
            for (int i = 0; i < snapshot.DeckCount; i++)
            {
                expansionIds[i] = snapshot.Deck[i].ExpansionId.Value;
                cardIds[i]      = snapshot.Deck[i].CardId;
                netKeys[i]      = snapshot.Deck[i].EntityKey.Value;
            }

            var handExpansionIds = new string[snapshot.HandCount];
            var handCardIds      = new int[snapshot.HandCount];
            var handNetKeys      = new string[snapshot.HandCount];
            for (int i = 0; i < snapshot.HandCount; i++)
            {
                handExpansionIds[i] = snapshot.Hand[i].ExpansionId.Value;
                handCardIds[i]      = snapshot.Hand[i].CardId;
                handNetKeys[i]      = snapshot.Hand[i].EntityKey.Value;
            }

            ref var sync         = ref syncPool.Add(opponentEntity);
            sync.DeckExpansionIds = expansionIds;
            sync.DeckCardIds      = cardIds;
            sync.DeckNetworkKeys  = netKeys;
            sync.DeckCount        = snapshot.DeckCount;
            sync.HandExpansionIds = handExpansionIds;
            sync.HandCardIds      = handCardIds;
            sync.HandNetworkKeys  = handNetKeys;
            sync.HandCount        = snapshot.HandCount;

            Debug.Log($"[PhotonRunHandler] RPC_SyncDeckSnapshot received from player {fromPlayerId}: {snapshot.DeckCount} deck + {snapshot.HandCount} hand cards");
        }


        private bool UpdatePlayerStage(PlayerRef player, PlayerLoadingStage newStage)
        {
            if (!_playerProgress.ContainsKey(player))
                return false;

            var playerInfo = _playerProgress[player];
            if (newStage <= playerInfo.LoadingStage)
                return false;

            playerInfo.LoadingStage = newStage;
            _playerProgress[player] = playerInfo;
            OnPlayerProgressChanged?.Invoke(player, newStage);
            return true;
        }
        
        private void SpawnCharForPlayers()
        { 

        }

        private bool AreAllPlayersAtStage(PlayerLoadingStage stage)
        {
            if (_playerProgress.Count == 0)
                return false;

            foreach (var kvp in _playerProgress)
            {
                if (kvp.Value.LoadingStage < stage)
                    return false;
            }
            return true;
        }

        private int GetPlayersAtStageCount(PlayerLoadingStage stage)
        {
            int count = 0;
            foreach (var kvp in _playerProgress)
            {
                if (kvp.Value.LoadingStage >= stage)
                    count++;
            }
            return count;
        } 

        private void OnDestroy()
        {
            // Выгружаем сцену при уничтожении handler'а
            /*if (_sceneHandle.IsValid())
            {
                Addressables.UnloadSceneAsync(_sceneHandle);
            }*/
        }
         
    }
}
