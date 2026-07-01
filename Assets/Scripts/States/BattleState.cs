using AwesomeUI.Core;
using AwesomeUI.Feature.Battle;
using Fusion;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using System;
using Game.Core.Ecs.Handlers;
using Game.Core.Events;
using Game.Core.Match;
using Game.Core.Mono;
using Game.Core.Network;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Game.Core.States
{
    public class BattleState : State, IGameStateContext, IBattleUIContext
    {
        public static new BattleState Instance
        {
            get
            {
                return (BattleState)State.Instance;
            }
        }

        public BoardView BoardView
        {
            get
            {
                if (_boardView == null) _boardView = FindFirstObjectByType<BoardView>();

                if (_boardView == null) throw new System.NullReferenceException($"{_boardView} not found on scene");

                return _boardView;
            }
        }

        [SerializeField] private BoardView _boardView;
        [SerializeField] private CardConfig _cardConfig;

        [HideInInspector] public EcsRunHandler EcsHandler;
        [HideInInspector] public PhotonRunHandler PhotonRunHandler;

        protected Dictionary<string, EcsPackedEntity> _localKeyMap = new();
        protected Dictionary<string, EcsPackedEntity> _netKeyMap = new();
        protected Dictionary<int, string> _netLocalMap = new();
        protected Dictionary<int, (PlayerRef playerRef, NetworkPlayerData playerData)> dictionaryPlayers = new Dictionary<int, (PlayerRef, NetworkPlayerData)>();
        public bool IsServer => PhotonRunHandler.IsServer;
        public EcsWorld World => EcsHandler.World;

        // Идёт выход в меню: гасим сессию и грузим MenuScene. Пока true — ECS не гоняем (раннер
        // завершается асинхронно, чужие RPC/системы в этот момент бессмысленны).
        private bool _exiting;

        public void AddEntity(int entity, string localKey = null, string networkKey = null)
        {
            var packed = EcsHandler.World.PackEntity(entity);

            if (!string.IsNullOrEmpty(localKey) && !_localKeyMap.ContainsKey(localKey))
                _localKeyMap[localKey] = packed;

            if (!string.IsNullOrEmpty(networkKey) && !_netKeyMap.ContainsKey(networkKey))
                _netKeyMap[networkKey] = packed;

            if (!_netLocalMap.ContainsKey(entity))
            {
                _netLocalMap[entity] = networkKey;
            }
        }

        public string GetNetEntityKey(int entity)
        {
            return _netLocalMap.TryGetValue(entity, out var key) ? key : null;
        }

        public bool TryGetPlayer(out int playerEntity)
        {
            return TryGetEntity("player", out playerEntity);
        }

        public bool TryGetEntity(string key, out int entity)
        {
            if (TryGetEntity(key, out EcsPackedEntity packed) && packed.Unpack(EcsHandler.World, out entity))
            {
                return true;
            }

            entity = -1;
            return false;
        }

        bool TryGetEntity(string key, out EcsPackedEntity packedEntity)
        {
            packedEntity = default;

            if (string.IsNullOrEmpty(key)) return false;

            return _localKeyMap.TryGetValue(key, out packedEntity) || _netKeyMap.TryGetValue(key, out packedEntity);
        }
        public override void Awake()
        {
            if (PhotonRunHandler == null)
            {
                PhotonRunHandler = FindFirstObjectByType<PhotonRunHandler>();

                if (PhotonRunHandler == null)
                {
                    Debug.LogError("[BattleState] PhotonRunHandler not found! Make sure it is spawned before BattleState.Start()");
                    return;
                }
            }

            MatchTracker.Initialize();
            // PhotonRunHandler — сетевой объект, может ещё не существовать в Awake.
            // Пробуем найти сразу, иначе будем искать в Start. 
            EcsHandler = EcsRunHandler.Create(this);
        }

        public override void Start()
        {
            // Повторный поиск на случай если в Awake handler ещё не заспавнился
            if (PhotonRunHandler == null)
            {
                PhotonRunHandler = FindFirstObjectByType<PhotonRunHandler>();

                if (PhotonRunHandler == null)
                {
                    Debug.LogError("[BattleState] PhotonRunHandler not found! Make sure it is spawned before BattleState.Start()");
                    return;
                }
            }

            UIModule.Open<BattleCanvas>();
            UIModule.Inject(this, this, EcsHandler.World, _cardConfig);

            // Подписываемся на ECS-init триггер от сервера.
            GameEventBus.Subscribe<TriggerStateInitEvent>(OnTriggerStateInit);
            GameEventBus.Subscribe<CellSelectedEvent>(OnCellSelected);
            GameEventBus.Subscribe<ExitToMenuRequestedEvent>(OnExitToMenuRequested);

            Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] Start(): subscribed to TriggerStateInitEvent. Calling RPC_NotifySceneLoaded.");

            // Сцена полностью загружена и Start() выполнен — уведомляем сервер.
            PhotonRunHandler.RPC_NotifySceneLoaded();

            Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] Start(): RPC_NotifySceneLoaded called.");
        }

        private void OnTriggerStateInit(TriggerStateInitEvent _)
        {
            Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: received. Starting EcsHandler.Init.");
            GameEventBus.Unsubscribe<TriggerStateInitEvent>(OnTriggerStateInit);

            try
            {
                EcsHandler.Init(PhotonRunHandler, BoardView, _cardConfig);
                PhotonRunHandler.RegisterGameState(this);
                Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: EcsHandler.Init completed successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: EcsHandler.Init FAILED: {e}");
            }

            Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: calling RPC_NotifyStateReady.");
            PhotonRunHandler.RPC_NotifyStateReady();
            Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: RPC_NotifyStateReady called.");
        }

        private void OnCellSelected(CellSelectedEvent evt)
        {
            if (EcsHandler?.World == null) return;
            int e = EcsHandler.World.NewEntity();
            ref var click = ref EcsHandler.World.GetPool<CellClickEvent>().Add(e);
            click.Row = evt.Row;
            click.Col = evt.Col;
            click.OwnerId = evt.OwnerId;
        }

        public override void OnDestroy()
        {
            GameEventBus.Unsubscribe<TriggerStateInitEvent>(OnTriggerStateInit);
            GameEventBus.Unsubscribe<CellSelectedEvent>(OnCellSelected);
            GameEventBus.Unsubscribe<ExitToMenuRequestedEvent>(OnExitToMenuRequested);
            EcsHandler?.Dispose();
            MatchTracker.Shutdown();
        }

        /// <summary>
        /// Выход в меню из поп-апа результата: чисто гасим Photon-сессию (EndSession ждёт штатного
        /// Runner.Shutdown), затем грузим MenuScene (build index 1). Загрузка сцены выгрузит боевую
        /// сцену → OnDestroy → EcsHandler.Dispose. PhotonInitializer (DontDestroyOnLoad) переживает
        /// переход; повторный matchmaking стартует с чистого состояния.
        /// </summary>
        private async void OnExitToMenuRequested(ExitToMenuRequestedEvent _)
        {
            if (_exiting) return;
            _exiting = true;
            GameEventBus.Unsubscribe<ExitToMenuRequestedEvent>(OnExitToMenuRequested);

            try
            {
                if (PhotonInitializer.Instance != null)
                    await PhotonInitializer.Instance.EndSession();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleState] EndSession on exit failed: {e.Message}");
            }

            RequestLoadingScene(1);   // MenuScene
        }

        public override void Update()
        {
            if (_exiting) return;   // сессия гаснет — пайплайн больше не гоняем
            EcsHandler.Run();
        }
        public void FixedUpdate() => EcsHandler.FixedRun();
        public void LateUpdate() => EcsHandler.LateRun();

        public bool TryGetPlayerEntity(out int playerEntity)
        {
            if (TryGetEntity(Service.EntityService.PLAYER_ENTITY, out playerEntity))
            {
                return true;
            }

            return false;
        }

        public bool TryGetOpponentEntity(out int opponentEntity)
        {
            if (TryGetEntity(Service.EntityService.OPPONENT_ENTITY, out opponentEntity))
            {
                return true;
            }

            return false;
        }

        public void CastEvent<TEvent>(TEvent evt) where TEvent : struct
        {
            EcsHandler.World.GetPool<TEvent>().Add(EcsHandler.World.NewEntity()) = evt;
        }
    }
}