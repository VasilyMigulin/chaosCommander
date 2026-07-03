using Fusion;
using Game.Core.Shared.Interface;
using Game.Core.Events;
using Leopotam.EcsLite;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MemoryPack;
using Game.Core.Network;
using Game.Core.Ecs.Components;
using System.Threading.Tasks;

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

        public void RegisterGameState(IGameStateContext state)
        {
            _state = state;
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RegisterGameState: _state assigned ({state.GetType().Name}).");
        }

        [Networked] public NetworkSessionData SessionData { get; set; }
        [Networked] public SessionState CurrentState { get; set; }
        [Networked] public int PlayersReady { get; set; }
        [Networked] public int PlayersSceneLoaded { get; set; }

        public float DeltaTime => runner.DeltaTime;
        public bool IsServer => runner.IsServer;

        public event System.Action<PlayerRef, PlayerLoadingStage> OnPlayerProgressChanged;

        private NetworkRunner runner;
        private bool _isLocalSceneLoaded;

        private readonly Dictionary<PlayerRef, NetworkPlayerInfo> _playerProgress = new Dictionary<PlayerRef, NetworkPlayerInfo>();
        public IReadOnlyDictionary<PlayerRef, NetworkPlayerInfo> PlayerProgress => _playerProgress;

        // Счётчик мулигана
        private int _mulliganReadyCount = 0;

        // TCS-гейты для async-пайплайна (только на сервере)
        private TaskCompletionSource<bool> _allPlayersConnectedTcs;
        private TaskCompletionSource<bool> _allSceneLoadedTcs;
        private TaskCompletionSource<bool> _allStateInitTcs;

        // VS-раскрытие командиров перед мулиганом: каждый клиент шлёт своего командира (RPC_SubmitCommander),
        // хост собирает и рассылает RPC_RevealCommanders. Только идентичности (exp/id) — симуляция не трогается.
        private TaskCompletionSource<bool> _allCommandersTcs;
        private readonly Dictionary<int, (string exp, int cardId)> _commanderSubmissions = new Dictionary<int, (string, int)>();
        const int CommanderRevealMs = 3000;

        private bool _pipelineStarted = false;
        private Task _sessionPipelineTask;

        public override void Spawned()
        {
            base.Spawned();
            runner = Runner;

            if (IsServer)
            {
                _playerProgress.Clear();
                CurrentState = SessionState.WaitingInLobby;
                PlayersSceneLoaded = 0;
                PlayersReady = 0;

                _allPlayersConnectedTcs = new TaskCompletionSource<bool>();
                _allSceneLoadedTcs     = new TaskCompletionSource<bool>();
                _allStateInitTcs       = new TaskCompletionSource<bool>();
                _allCommandersTcs      = new TaskCompletionSource<bool>();
                _commanderSubmissions.Clear();

                // Подписываемся на вход игроков
                if (PhotonInitializer.Instance != null)
                    PhotonInitializer.Instance.OnPlayerJoinedEvent += OnPlayerJoinedHandler;

                // Регистрируем уже подключённых игроков
                foreach (var player in runner.ActivePlayers)
                    EnsurePlayerRegistered(player);

                Debug.Log($"[PhotonRunHandler][HOST] Spawned with {_playerProgress.Count} players registered");

                // Проверяем: вдруг уже все игроки на месте
                TryFireAllPlayersConnected();

                _sessionPipelineTask = RunSessionPipelineAsync();
            }

            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] Spawned. IsServer={Runner.IsServer} LocalPlayer={Runner.LocalPlayer}");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            base.Despawned(runner, hasState);
            _pipelineStarted = false;

            if (PhotonInitializer.Instance != null)
                PhotonInitializer.Instance.OnPlayerJoinedEvent -= OnPlayerJoinedHandler;

            // Отменяем незавершённые ожидания
            _allPlayersConnectedTcs?.TrySetCanceled();
            _allSceneLoadedTcs?.TrySetCanceled();
            _allStateInitTcs?.TrySetCanceled();
            _allCommandersTcs?.TrySetCanceled();
        }

        /// <summary>
        /// Регистрирует игрока при подключении и проверяет готовность к старту.
        /// </summary>
        private void OnPlayerJoinedHandler(PlayerRef player)
        {
            if (!IsServer) return;
            EnsurePlayerRegistered(player);
            TryFireAllPlayersConnected();
        }

        private void EnsurePlayerRegistered(PlayerRef player)
        {
            if (_playerProgress.ContainsKey(player))
                return;

            _playerProgress[player] = new NetworkPlayerInfo
            {
                LoadingStage = PlayerLoadingStage.Connected
            };
            Debug.Log($"[PhotonRunHandler] Registered player {player}, total: {_playerProgress.Count}");
        }

        private void TryFireAllPlayersConnected()
        {
            if (!IsServer || _pipelineStarted) return;

            int expected = SessionData.TargetPlayerCount > 0 ? SessionData.TargetPlayerCount : 2;
            if (_playerProgress.Count < expected)
            {
                Debug.Log($"[PhotonRunHandler][HOST] TryFireAllPlayersConnected: waiting {_playerProgress.Count}/{expected}. Known players: [{string.Join(", ", _playerProgress.Keys)}]");
                return;
            }

            _pipelineStarted = true;
            Debug.Log($"[PhotonRunHandler][HOST] TryFireAllPlayersConnected: all {_playerProgress.Count} players connected. Unblocking pipeline.");
            _allPlayersConnectedTcs?.TrySetResult(true);
        }

        /// <summary>
        /// Главный async-пайплайн загрузки сессии (выполняется только на сервере).
        /// Порядок: ждём всех игроков → загружаем боевую сцену → инициализируем ECS → стартуем игру.
        /// </summary>
        private async Task RunSessionPipelineAsync()
        {
            try
            {
                // Шаг 1: ждём подключения всех игроков
                Debug.Log("[Pipeline][HOST] Step 1: Waiting for all players to connect...");
                await _allPlayersConnectedTcs.Task;
                Debug.Log($"[Pipeline][HOST] Step 1 DONE: All players connected. Count={_playerProgress.Count}");

                // Шаг 2: просим всех загрузить боевую сцену и ждём подтверждения
                CurrentState = SessionState.LoadingGameScene;
                Debug.Log("[Pipeline][HOST] Step 2: -> LoadingGameScene. Sending RPC_LoadGameScene.");
                RPC_LoadGameScene();

                Debug.Log("[Pipeline][HOST] Step 2: Waiting for all players to load scene...");
                await _allSceneLoadedTcs.Task;
                Debug.Log($"[Pipeline][HOST] Step 2 DONE: All players loaded scene. PlayersSceneLoaded={PlayersSceneLoaded}");

                // Шаг 3: просим всех инициализировать ECS / BattleState и ждём подтверждения
                CurrentState = SessionState.InitializingGameState;
                Debug.Log("[Pipeline][HOST] Step 3: -> InitializingGameState. Sending RPC_TriggerStateInit.");
                RPC_TriggerStateInit();

                Debug.Log("[Pipeline][HOST] Step 3: Waiting for all players state init...");
                await _allStateInitTcs.Task;
                Debug.Log($"[Pipeline][HOST] Step 3 DONE: All players state initialized. PlayersReady={PlayersReady}");

                // Шаг 3.5: обмен командирами → VS-раскрытие ПЕРЕД мулиганом.
                // Командиры фиксированы на сборке колоды и известны каждому клиенту сразу после state-init,
                // поэтому их можно раскрыть до снапшота руки (тот приедет после мулигана). Таймаут 5с —
                // чтобы не подвиснуть, если чей-то submit не дошёл (клиенты просто не покажут пустую карту).
                Debug.Log("[Pipeline][HOST] Step 3.5: waiting for both commanders...");
                await Task.WhenAny(_allCommandersTcs.Task, Task.Delay(5000));

                BuildRevealArgs(out int p1Id, out string p1Exp, out int p1CardId,
                                out int p2Id, out string p2Exp, out int p2CardId);
                Debug.Log($"[Pipeline][HOST] Step 3.5: reveal commanders p1={p1Exp}:{p1CardId} p2={p2Exp}:{p2CardId}");
                RPC_RevealCommanders(p1Id, p1Exp ?? string.Empty, p1CardId, p2Id, p2Exp ?? string.Empty, p2CardId);

                // Дать игрокам разглядеть VS-экран, потом стартуем мулиган (VS скроется по MulliganStartedEvent).
                await Task.Delay(CommanderRevealMs);

                // Шаг 4: старт игры / муллиган
                CurrentState = SessionState.GameStarted;
                Debug.Log("[Pipeline][HOST] Step 4: -> GameStarted. Sending RPC_StartGame.");
                RPC_StartGame();
                Debug.Log("[Pipeline][HOST] Step 4 DONE: Pipeline complete.");
            }
            catch (System.Exception ex) when (!(ex is System.OperationCanceledException))
            {
                Debug.LogError($"[Pipeline][HOST] Exception in RunSessionPipelineAsync: {ex}");
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_LoadGameScene()
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_LoadGameScene received. LocalPlayer={Runner.LocalPlayer}");
            LoadGameScene();
        }

        private void LoadGameScene()
        {
            if (_isLocalSceneLoaded)
            {
                Debug.LogWarning($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] LoadGameScene: scene already loaded, skipping.");
                return;
            }

            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] LoadGameScene: starting additive load of scene index 3.");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(3, LoadSceneMode.Additive);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLocalSceneLoaded = true;
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] OnSceneLoaded: scene='{scene.name}'. Waiting for BattleState.Start() to call RPC_NotifySceneLoaded.");
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_NotifySceneLoaded(RpcInfo info = default)
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_NotifySceneLoaded received. Source={info.Source} LocalPlayer={Runner.LocalPlayer}");

            if (!IsServer) return;

            PlayerRef player = info.Source;
            if (player == PlayerRef.None)
            {
                Debug.LogWarning($"[PhotonRunHandler][HOST] RPC_NotifySceneLoaded: info.Source is None, falling back to LocalPlayer={Runner.LocalPlayer}");
                player = Runner.LocalPlayer;
            }

            Debug.Log($"[PhotonRunHandler][HOST] RPC_NotifySceneLoaded: processing player={player}. Known players: [{string.Join(", ", _playerProgress.Keys)}]");

            if (UpdatePlayerStage(player, PlayerLoadingStage.SceneLoaded))
            {
                PlayersSceneLoaded = GetPlayersAtStageCount(PlayerLoadingStage.SceneLoaded);
                Debug.Log($"[PhotonRunHandler][HOST] Player {player} scene loaded: {PlayersSceneLoaded}/{_playerProgress.Count}");

                if (AreAllPlayersAtStage(PlayerLoadingStage.SceneLoaded))
                {
                    Debug.Log("[Pipeline][HOST] All players scene loaded — unblocking pipeline.");
                    _allSceneLoadedTcs?.TrySetResult(true);
                }
                else
                {
                    Debug.Log($"[Pipeline][HOST] Still waiting for scene load: {PlayersSceneLoaded}/{_playerProgress.Count}");
                }
            }
            else
            {
                Debug.LogWarning($"[PhotonRunHandler][HOST] RPC_NotifySceneLoaded: UpdatePlayerStage rejected for player={player} (already at this stage or higher)");
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TriggerStateInit()
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_TriggerStateInit received. Publishing TriggerStateInitEvent.");
            GameEventBus.Publish(new TriggerStateInitEvent());
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] TriggerStateInitEvent published.");
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_NotifyStateReady(RpcInfo info = default)
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_NotifyStateReady received. Source={info.Source} LocalPlayer={Runner.LocalPlayer}");

            if (!IsServer) return;

            PlayerRef player = info.Source;
            if (player == PlayerRef.None)
            {
                Debug.LogWarning($"[PhotonRunHandler][HOST] RPC_NotifyStateReady: info.Source is None, falling back to LocalPlayer={Runner.LocalPlayer}");
                player = Runner.LocalPlayer;
            }

            Debug.Log($"[PhotonRunHandler][HOST] RPC_NotifyStateReady: processing player={player}. Known players: [{string.Join(", ", _playerProgress.Keys)}]");

            if (UpdatePlayerStage(player, PlayerLoadingStage.StateInitialized))
            {
                PlayersReady = GetPlayersAtStageCount(PlayerLoadingStage.StateInitialized);
                Debug.Log($"[PhotonRunHandler][HOST] Player {player} state initialized: {PlayersReady}/{_playerProgress.Count}");

                if (AreAllPlayersAtStage(PlayerLoadingStage.StateInitialized))
                {
                    Debug.Log("[Pipeline][HOST] All players state initialized — unblocking pipeline.");
                    _allStateInitTcs?.TrySetResult(true);
                }
                else
                {
                    Debug.Log($"[Pipeline][HOST] Still waiting for state init: {PlayersReady}/{_playerProgress.Count}");
                }
            }
            else
            {
                Debug.LogWarning($"[PhotonRunHandler][HOST] RPC_NotifyStateReady: UpdatePlayerStage rejected for player={player} (already at this stage or higher)");
            }
        }
         

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StartGame()
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_StartGame received — mulligan phase begins.");

            // VS-раскрытие закончилось → снимаем крышку: VS-окно закрывается, под ним проявляется
            // мулиган (он уже открыт с EcsHandler.Init, но был накрыт). Синхронно на обоих клиентах.
            GameEventBus.Publish(new MulliganPhaseBeginUIEvent());
        }

        // ── VS-раскрытие командиров (перед мулиганом) ─────────────────────────

        /// <summary>Каждый клиент шлёт идентичность СВОЕГО командира хосту (вызывается после state-init).</summary>
        public void SubmitLocalCommander()
        {
            if (!TryGetLocalCommander(out int playerId, out string exp, out int cardId))
            {
                Debug.LogWarning("[PhotonRunHandler] SubmitLocalCommander: локальный командир не найден");
                return;
            }
            RPC_SubmitCommander(playerId, exp ?? string.Empty, cardId);
        }

        bool TryGetLocalCommander(out int playerId, out string exp, out int cardId)
        {
            playerId = -1; exp = null; cardId = -1;

            var world = _state?.World;
            if (world == null) return false;

            int localId = LocalPlayerId();
            if (localId < 0) return false;

            var ownerPool = world.GetPool<Game.Core.Ecs.Components.OwnerComponent>();
            var modelPool = world.GetPool<Game.Core.Ecs.Components.CardModelComponent>();

            foreach (var e in world.Filter<Game.Core.Ecs.Components.CommanderTag>()
                                    .Inc<Game.Core.Ecs.Components.CardModelComponent>().End())
            {
                if (!ownerPool.Has(e) || ownerPool.Get(e).OwnerId != localId) continue;

                ref var m = ref modelPool.Get(e);
                playerId = localId;
                exp      = m.ExpansionId;
                cardId   = m.ModelId;
                return true;
            }
            return false;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SubmitCommander(int playerId, string exp, int cardId, RpcInfo info = default)
        {
            if (!IsServer) return;

            _commanderSubmissions[playerId] = (exp, cardId);
            Debug.Log($"[PhotonRunHandler][HOST] Commander submitted: player={playerId} {exp}:{cardId} ({_commanderSubmissions.Count}/{_playerProgress.Count})");

            if (_commanderSubmissions.Count >= _playerProgress.Count)
                _allCommandersTcs?.TrySetResult(true);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_RevealCommanders(int p1Id, string p1Exp, int p1CardId, int p2Id, string p2Exp, int p2CardId)
        {
            int localId = LocalPlayerId();

            string localExp, oppExp;
            int localCard, oppCard;
            if (localId == p1Id)
            {
                localExp = p1Exp; localCard = p1CardId; oppExp = p2Exp; oppCard = p2CardId;
            }
            else
            {
                localExp = p2Exp; localCard = p2CardId; oppExp = p1Exp; oppCard = p1CardId;
            }

            Debug.Log($"[VS] RPC_RevealCommanders on {(Runner.IsServer ? "HOST" : "CLIENT")}: localId={localId} local={localExp}:{localCard} opp={oppExp}:{oppCard} → publish CommandersRevealedUIEvent");
            GameEventBus.Publish(new CommandersRevealedUIEvent
            {
                LocalExpansionId    = localExp,
                LocalCardId         = localCard,
                OpponentExpansionId = oppExp,
                OpponentCardId      = oppCard,
            });
        }

        void BuildRevealArgs(out int p1Id, out string p1Exp, out int p1CardId,
                             out int p2Id, out string p2Exp, out int p2CardId)
        {
            p1Id = 1; p2Id = 2;
            (p1Exp, p1CardId) = _commanderSubmissions.TryGetValue(1, out var a) ? (a.exp, a.cardId) : (string.Empty, -1);
            (p2Exp, p2CardId) = _commanderSubmissions.TryGetValue(2, out var b) ? (b.exp, b.cardId) : (string.Empty, -1);
        }
         
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_NotifyMulliganReady(RpcInfo info = default)
        {
            Debug.Log($"[PhotonRunHandler][{(Runner.IsServer ? "HOST" : "CLIENT")}] RPC_NotifyMulliganReady received. Source={info.Source}");

            if (!IsServer) return;

            _mulliganReadyCount++;
            Debug.Log($"[PhotonRunHandler][HOST] MulliganReady {_mulliganReadyCount}/{_playerProgress.Count}");

            if (_mulliganReadyCount >= _playerProgress.Count)
            {
                _mulliganReadyCount = 0;
                RPC_AllMulligansCompleted();
            }
        }

        /// <summary>���� > ��� �������: ��� �������� ���������, ��������� ������ ���.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AllMulligansCompleted()
        {
            Debug.Log("[PhotonRunHandler] All mulligans completed! Starting first turn.");
            GameEventBus.Publish(new AllMulligansCompletedEvent());

            if (IsServer)
                StartFirstTurn();
        }

        // -- Turn coordination ------------------------------------------------

        private int _currentTurnNumber = 0;
        private int _activePlayerId = -1;

        /// <summary>���� ��������� ������� ������ (���� ������ ����� ������) � ��������� MatchStart-����.</summary>
        private void StartFirstTurn()
        {
            if (!IsServer || _state == null) return;

            var world = _state.World;
            var playerPool = world.GetPool<PlayerComponent>();
            var sidePool   = world.GetPool<PlayerSideComponent>();
            var filter = world.Filter<PlayerComponent>()
                              .Inc<PlayerSideComponent>().End();

            int firstPlayerId = -1;
            int firstPersonalTurn = 1;
             
            foreach (var e in filter)
            {
                if (sidePool.Get(e).Side == 1)
                {
                    firstPlayerId = playerPool.Get(e).PlayerId;
                    break;
                }
            }
             
            if (firstPlayerId == -1)
            {
                var filterAll = world.Filter<Game.Core.Ecs.Components.PlayerComponent>().End();
                foreach (var e in filterAll)
                {
                    int pid = playerPool.Get(e).PlayerId;
                    if (firstPlayerId == -1 || pid < firstPlayerId)
                        firstPlayerId = pid;
                }
            }

            _currentTurnNumber = 1;
            _activePlayerId = firstPlayerId;

            RPC_PreStartPhaseBegin(firstPlayerId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_PreStartPhaseBegin(int firstPlayerId)
        {
            if (_state != null)
            {
                int playerEntityFirst = -1;
                int playerEntitySecond = -1;

                var world = _state.World;

                var filter = world.Filter<PlayerComponent>().End();

                foreach (var playerEntity in filter)
                {
                    ref var playerComp = ref world.GetPool<PlayerComponent>().Get(playerEntity);

                    if (playerComp.PlayerId == 1)
                    {
                        playerEntityFirst = playerEntity;
                    }
                    else
                    {
                        playerEntitySecond = playerEntity;
                    }
                }

                if(playerEntityFirst != -1) world.GetPool<MatchStartEvent>().Add(playerEntityFirst);
                if(playerEntitySecond != -1) world.GetPool<MatchStartEvent>().Add(playerEntitySecond);

                // UI начальной руки
                PublishPreStartHandUI(world);

                // Первый ход: на клиенте, где первый игрок ЛОКАЛЬНЫЙ, запускаем каскад начала хода
                // (StartTurnState → ресурсы/добор/OnTurnStart → ActiveState). Никаких фаз/handshake.
                var playerPool = world.GetPool<PlayerComponent>();
                foreach (var pe in world.Filter<PlayerComponent>().Inc<LocalComponent>().End())
                {
                    if (playerPool.Get(pe).PlayerId == firstPlayerId)
                    {
                        Game.Core.Ecs.Components.TurnFlow.GrantTurn(world, pe, 1);
                        Debug.Log($"[PhotonRunHandler] First turn granted to local player {firstPlayerId}");
                    }
                    else
                    {
                        // Локальный игрок не первый → у него ход оппонента: показываем попап.
                        GameEventBus.Publish(new Game.Core.Events.OpponentTurnEndedEvent());
                        Debug.Log("[PhotonRunHandler] First turn: local is passive → opponent-turn popup");
                    }
                }
            }
        }

        private void PublishPreStartHandUI(EcsWorld world)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var handPool = world.GetPool<HandComponent>();
            var viewPool = world.GetPool<CardViewDataComponent>();
            var netKeyPool = world.GetPool<NetworkEntityComponent>();
            var commandPool = world.GetPool<CommanderTag>();

            var localFilter = world.Filter<PlayerComponent>().Inc<HandComponent>().Inc<LocalComponent>().End();

            foreach (var playerEntity in localFilter)
            {
                ref var player = ref playerPool.Get(playerEntity);
                ref var hand = ref handPool.Get(playerEntity);

                var handCards = new List<CardAddedToHandUIEvent>();
                CardAddedToHandUIEvent commanderCard = default;
                bool hasCommander = false;

                for (int i = 0; i < hand.Count; i++)
                {
                    int cardEntity = hand.CardEntities[i];
                    if (!viewPool.Has(cardEntity)) continue;

                    ref var view = ref viewPool.Get(cardEntity);
                    string netKey = netKeyPool.Has(cardEntity) ? netKeyPool.Get(cardEntity).NetworkEntityKey : string.Empty;

                    bool isCommander = commandPool.Has(cardEntity);
                    var evt = new CardAddedToHandUIEvent
                    {
                        CardEntity  = cardEntity,
                        PlayerId    = player.PlayerId,
                        NetworkKey  = netKey,
                        Icon        = view.ArtImage,
                        CardType    = view.CardType,
                        Element     = view.Element,
                        Rarity      = view.Rarity,
                        CardName    = view.CardName,
                        IsCommander = isCommander,
                        Visual      = new Game.Core.Shared.CardVisualData
                        {
                            CardName    = view.CardName,
                            Description = view.Description,
                            Icon        = view.ArtImage,
                            CardType    = view.CardType,
                            Rarity      = view.Rarity,
                            Element     = view.Element,
                            CostType    = view.CostType,
                            CostAmount  = view.CostAmount,
                            IsCreature  = view.IsCreature,
                            Attack      = view.Attack,
                            MaxHealth   = view.MaxHealth,
                            Speed       = view.Speed,
                            IsCommander = isCommander,
                        },
                    };

                    if (commandPool.Has(cardEntity))
                    {
                        commanderCard = evt;
                        hasCommander = true;
                    }
                    else
                    {
                        handCards.Add(evt);
                    }
                }

                GameEventBus.Publish(new PreStartPhaseBeginUIEvent
                {
                    HandCards = handCards.ToArray(),
                    CommanderCard = commanderCard,
                    HasCommander = hasCommander,
                });

                break; // ������ ��������� �����
            }
        }


        /// <summary>
        /// ��������� ������ �������� �� ����� � ���������� �� ����� RPC.
        /// </summary>
        public void SendDeckSnapshotChunked(byte[] data, int fromPlayerId, int chunkSize = 400)
        {
            int totalChunks = (data.Length + chunkSize - 1) / chunkSize;
            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * chunkSize;
                int length = Math.Min(chunkSize, data.Length - start);
                var chunk = new byte[length];
                Buffer.BlockCopy(data, start, chunk, 0, length);
                RPC_SyncDeckSnapshotChunk(chunk, i, totalChunks, fromPlayerId);
            }
        }

        /// <summary>
        /// ��������� ������ ���������� ������ ����� ������ ��������� ������� �� 400 ����.
        /// ���������� ����� ���������� ��������.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SyncDeckSnapshotChunk(byte[] chunk, int chunkIndex, int totalChunks, int fromPlayerId, RpcInfo info = default)
        {
            if (_state == null)
                return;

            // СВОЙ снапшот игнорируем. RPC идёт RpcTargets.All (включая отправителя), а на ХОСТЕ info.Source
            // собственного RPC приходит None (см. тот же эффект в RPC_NotifySceneLoaded) → сравнение
            // info.Source == LocalPlayer НЕ ловит свой снапшот, и хост строил зеркало оппонента из СВОИХ карт
            // (owner=оппонент, но СВОИ ключи "1-*") → коллизия netKey → дискард/таргет по руке оппонента бил по
            // фантомной копии своей руки и промахивался на клиенте. Сверяем ЯВНЫЙ fromPlayerId с локальным id.
            if (fromPlayerId == LocalPlayerId())
                return;

            if (_snapshotChunks == null || _snapshotChunks.Length != totalChunks)
            {
                _snapshotChunks = new byte[totalChunks][];
                _snapshotChunksReceived = 0;
                _snapshotFromPlayerId = fromPlayerId;
            }

            if (_snapshotChunks[chunkIndex] == null)
            {
                _snapshotChunks[chunkIndex] = chunk;
                _snapshotChunksReceived++;
            }

            if (_snapshotChunksReceived < totalChunks)
                return;
             
            int totalLength = 0;
            for (int i = 0; i < totalChunks; i++)
                totalLength += _snapshotChunks[i].Length;

            var fullData = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < totalChunks; i++)
            {
                Buffer.BlockCopy(_snapshotChunks[i], 0, fullData, offset, _snapshotChunks[i].Length);
                offset += _snapshotChunks[i].Length;
            }

            _snapshotChunks = null;
            _snapshotChunksReceived = 0;

            ApplyDeckSnapshot(fullData, _snapshotFromPlayerId);
        }

        byte[][] _snapshotChunks;
        int _snapshotChunksReceived;
        int _snapshotFromPlayerId;

        // PlayerId локального игрока из ECS (PlayerComponent+LocalComponent). Надёжнее info.Source/Runner.LocalPlayer
        // для отсева СВОИХ RpcTargets.All-сообщений (на хосте info.Source своего RPC = None).
        int LocalPlayerId()
        {
            var world = _state?.World;
            if (world == null) return -1;
            var playerPool = world.GetPool<Game.Core.Ecs.Components.PlayerComponent>();
            foreach (var pe in world.Filter<Game.Core.Ecs.Components.PlayerComponent>().Inc<Game.Core.Ecs.Components.LocalComponent>().End())
                return playerPool.Get(pe).PlayerId;
            return -1;
        }

        void ApplyDeckSnapshot(byte[] data, int fromPlayerId)
        {
            // Защита в глубину: не строим зеркало оппонента из СВОЕГО снапшота (см. guard в чанк-хендлере).
            if (_state == null || fromPlayerId == LocalPlayerId())
                return;

            NetworkDeckSnapshotData snapshot = MemoryPackSerializer.Deserialize<NetworkDeckSnapshotData>(data);

            if (_state.TryGetOpponentEntity(out int opponentEntity))
            {
                ref var deckSyncComp = ref _state.World.GetPool<OpponentDeckSyncEvent>().Add(opponentEntity);

                var expansionIds = new string[snapshot.DeckCount];
                var cardIds = new int[snapshot.DeckCount];
                var netKeys = new string[snapshot.DeckCount];
                for (int i = 0; i < snapshot.DeckCount; i++)
                {
                    expansionIds[i] = snapshot.Deck[i].ExpansionId;
                    cardIds[i] = snapshot.Deck[i].CardId;
                    netKeys[i] = snapshot.Deck[i].EntityKey;
                }

                var handExpansionIds = new string[snapshot.HandCount];
                var handCardIds = new int[snapshot.HandCount];
                var handNetKeys = new string[snapshot.HandCount];
                for (int i = 0; i < snapshot.HandCount; i++)
                {
                    handExpansionIds[i] = snapshot.Hand[i].ExpansionId;
                    handCardIds[i] = snapshot.Hand[i].CardId;
                    handNetKeys[i] = snapshot.Hand[i].EntityKey;
                }

                deckSyncComp.DeckExpansionIds = expansionIds;
                deckSyncComp.DeckCardIds = cardIds;
                deckSyncComp.DeckNetworkKeys = netKeys;
                deckSyncComp.DeckCount = snapshot.DeckCount;
                deckSyncComp.HandExpansionIds = handExpansionIds;
                deckSyncComp.HandCardIds = handCardIds;
                deckSyncComp.HandNetworkKeys = handNetKeys;
                deckSyncComp.HandCount = snapshot.HandCount;
                deckSyncComp.CommanderExpansionID = snapshot.Commander.ExpansionId;
                deckSyncComp.CommanderID = snapshot.Commander.CardId;
                deckSyncComp.CommanderNetKey = snapshot.Commander.EntityKey;

                Debug.Log($"[PhotonRunHandler] RPC_SyncDeckSnapshot received from player {fromPlayerId}: {snapshot.DeckCount} deck + {snapshot.HandCount} hand cards");
            }
        }

        [Obsolete("Use RPC_SyncDeckSnapshotChunk instead")] 
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SyncDeckSnapshot(byte[] data, int fromPlayerId, RpcInfo info = default)
        {
            NetworkDeckSnapshotData snapshot = MemoryPackSerializer.Deserialize<NetworkDeckSnapshotData>(data);

            var runner = Runner;
            if (runner != null && info.Source == runner.LocalPlayer)
                return;

            if (_state == null)
                return;

            if (_state.TryGetOpponentEntity(out int opponentEntity))
            {
                ref var deckSyncComp = ref _state.World.GetPool<OpponentDeckSyncEvent>().Add(opponentEntity);

                var expansionIds = new string[snapshot.DeckCount];
                var cardIds = new int[snapshot.DeckCount];
                var netKeys = new string[snapshot.DeckCount];
                for (int i = 0; i < snapshot.DeckCount; i++)
                {
                    expansionIds[i] = snapshot.Deck[i].ExpansionId;
                    cardIds[i] = snapshot.Deck[i].CardId;
                    netKeys[i] = snapshot.Deck[i].EntityKey;
                }

                var handExpansionIds = new string[snapshot.HandCount];
                var handCardIds = new int[snapshot.HandCount];
                var handNetKeys = new string[snapshot.HandCount];
                for (int i = 0; i < snapshot.HandCount; i++)
                {
                    handExpansionIds[i] = snapshot.Hand[i].ExpansionId;
                    handCardIds[i] = snapshot.Hand[i].CardId;
                    handNetKeys[i] = snapshot.Hand[i].EntityKey;
                }

                deckSyncComp.DeckExpansionIds = expansionIds;
                deckSyncComp.DeckCardIds = cardIds;
                deckSyncComp.DeckNetworkKeys = netKeys;
                deckSyncComp.DeckCount = snapshot.DeckCount;
                deckSyncComp.HandExpansionIds = handExpansionIds;
                deckSyncComp.HandCardIds = handCardIds;
                deckSyncComp.HandNetworkKeys = handNetKeys;
                deckSyncComp.HandCount = snapshot.HandCount;
                deckSyncComp.CommanderExpansionID = snapshot.Commander.ExpansionId;
                deckSyncComp.CommanderID = snapshot.Commander.CardId;
                deckSyncComp.CommanderNetKey = snapshot.Commander.EntityKey;

                Debug.Log($"[PhotonRunHandler] RPC_SyncDeckSnapshot received from player {fromPlayerId}: {snapshot.DeckCount} deck + {snapshot.HandCount} hand cards");
            } 
        }

        /// <summary>
        /// Отправляет снапшот одного действия активного игрока оппоненту.
        /// Получатель кладёт снапшот в ActionQueue для воспроизведения через ReplayActionSystem.
        /// </summary>
        // InvokeLocal = false: RPC НЕ выполняется у отправителя — он уже совершил действие локально,
        // реплеить своё нельзя (иначе резолв→коллектор→отправка зацикливаются). Это надёжнее фильтра
        // по info.Source, т.к. у хоста (StateAuthority) Source == None и на отправителе, и на получателе
        // — фильтром их не различить.
        [Rpc(RpcSources.All, RpcTargets.All, InvokeLocal = false)]
        public void RPC_SendActionSnapshot(byte[] data, RpcInfo info = default)
        {
            if (_state == null)
                return;

            var snapshot = MemoryPackSerializer.Deserialize<IActionData>(data);
            ActionQueue.Enqueue(snapshot);
        }
         
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_NotifyCardCast(string cardEntityKey, string targetEntityKey, int targetCell, RpcInfo info = default)
        {
            var runner = Runner;
            if (runner != null && info.Source == runner.LocalPlayer)
                return;

            if (_state == null)
            { 
                return;
            }

            GameEventBus.Publish(new RemoteCardCastEvent
            {
                CardEntityKey   = cardEntityKey,
                TargetEntityKey = targetEntityKey,
                TargetCell      = targetCell
            });
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_NotifyCreatureMove(string creatureEntityKey, int toRow, int toCol, int toOwnerId, RpcInfo info = default)
        {
            var runner = Runner;
            if (runner != null && info.Source == runner.LocalPlayer)
                return;

            if (_state == null)
                return;

            GameEventBus.Publish(new Game.Core.Events.RemoteCreatureMoveEvent
            {
                CreatureEntityKey = creatureEntityKey,
                ToRow             = toRow,
                ToCol             = toCol,
                ToOwnerId         = toOwnerId,
            });
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_NotifyCreatureAttack(string attackerEntityKey, string defenderEntityKey, RpcInfo info = default)
        {
            var runner = Runner;
            if (runner != null && info.Source == runner.LocalPlayer)
                return;

            if (_state == null)
                return;

            GameEventBus.Publish(new Game.Core.Events.RemoteCreatureAttackEvent
            {
                AttackerEntityKey = attackerEntityKey,
                DefenderEntityKey = defenderEntityKey,
            });
        }

        private bool UpdatePlayerStage(PlayerRef player, PlayerLoadingStage newStage)
        {
            // Upsert: ���� ����� �� ��������������� � ������������ �� ����.
            // ��� ������ �� race condition ����� RPC �������� ������ ��� OnPlayerJoined.
            if (!_playerProgress.ContainsKey(player))
            {
                Debug.LogWarning($"[PhotonRunHandler] Player {player} not in progress dict, registering on-the-fly.");
                _playerProgress[player] = new NetworkPlayerInfo { LoadingStage = PlayerLoadingStage.Connected };
            }

            var playerInfo = _playerProgress[player];
            if (newStage <= playerInfo.LoadingStage)
                return false;

            playerInfo.LoadingStage = newStage;
            _playerProgress[player] = playerInfo;
            OnPlayerProgressChanged?.Invoke(player, newStage);
            return true;
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
            // ��������� ����� ��� ����������� handler'�
            /*if (_sceneHandle.IsValid())
            {
                Addressables.UnloadSceneAsync(_sceneHandle);
            }*/
        }
         
    }
}
