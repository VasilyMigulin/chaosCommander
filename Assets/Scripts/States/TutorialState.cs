using AwesomeUI.Core;
using AwesomeUI.Feature.Battle;
using Game.Core.Configs;
using Game.Core.Ecs.Handlers;
using Game.Core.Events;
using Game.Core.Match;
using Game.Core.Mono;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.States
{
    /// <summary>
    /// Состояние сцены ТУТОРИАЛА (TutorialScene, build index 4) — полностью отвязано от BattleState/Photon:
    /// свой TutorialEcsHandler (без сети/мулигана/ИИ-мозга), фиксированные колоды из туториального
    /// энкаунтера (_tutorialEncounter: PlayerDeck игрока + колода-груша), сценарий и подсказки ведёт
    /// TutorialDirectorSystem. Выход (попап результата → «в меню») — на сцену логина (0): при победе
    /// FirstRunFlow.TutorialDone=true → обычный логин; при поражении флаг не ставится → роутинг
    /// InitState вернёт в туториал.
    /// На сцен-объекте (вместо BattleState): _boardView, _cardConfig, _tutorialEncounter.
    /// </summary>
    public class TutorialState : State, IGameStateContext, IBattleUIContext
    {
        [SerializeField] private BoardView _boardView;
        [SerializeField] private CardConfig _cardConfig;
        [SerializeField] private PveEncounterConfig _tutorialEncounter;

        public TutorialEcsHandler EcsHandler { get; private set; }

        readonly Dictionary<string, EcsPackedEntity> _localKeyMap = new();
        readonly Dictionary<string, EcsPackedEntity> _netKeyMap = new();
        readonly Dictionary<int, string> _netLocalMap = new();

        public bool IsServer => true;                     // локальная симуляция
        public EcsWorld World => EcsHandler.World;

        public BoardView BoardView
        {
            get
            {
                if (_boardView == null) _boardView = FindFirstObjectByType<BoardView>();
                return _boardView;
            }
        }

        public override void Awake()
        {
            // Туториал ездит на PvE-механизме: фиксированные колоды/гейты берутся из энкаунтера.
            Game.Core.Service.PveMode.Enabled = true;
            Game.Core.Service.PveMode.EncounterAsset = _tutorialEncounter;
            Game.Core.Service.PveMode.EncounterId = "tutorial";

            MatchTracker.Initialize();
            EcsHandler = new TutorialEcsHandler(this);
        }

        public override void Start()
        {
            UIModule.Open<BattleCanvas>();
            UIModule.Inject(this, this, EcsHandler.World, _cardConfig);

            GameEventBus.Subscribe<CellSelectedEvent>(OnCellSelected);
            GameEventBus.Subscribe<ExitToMenuRequestedEvent>(OnExitToMenu);

            try
            {
                EcsHandler.Init(BoardView, _cardConfig);
                Debug.Log("[TutorialState] EcsHandler.Init completed (туториал).");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TutorialState] EcsHandler.Init FAILED: {e}");
            }
        }

        public override void Update() => EcsHandler?.Run();

        public override void OnDestroy()
        {
            GameEventBus.Unsubscribe<CellSelectedEvent>(OnCellSelected);
            GameEventBus.Unsubscribe<ExitToMenuRequestedEvent>(OnExitToMenu);
            EcsHandler?.Dispose();
            MatchTracker.Shutdown();
            Game.Core.Service.PveMode.Reset();
        }

        void OnCellSelected(CellSelectedEvent evt)
        {
            if (EcsHandler?.World == null) return;
            int e = EcsHandler.World.NewEntity();
            ref var click = ref EcsHandler.World.GetPool<Game.Core.Ecs.Components.CellClickEvent>().Add(e);
            click.Row = evt.Row;
            click.Col = evt.Col;
            click.OwnerId = evt.OwnerId;
        }

        // Выход из туториала (попап результата): на сцену логина. TutorialDone поставил директор при
        // победе; при поражении роутинг InitState вернёт сюда снова.
        void OnExitToMenu(ExitToMenuRequestedEvent _)
        {
            RequestLoadingScene(0);
        }

        // ── IGameStateContext ────────────────────────────────────────────────

        public void AddEntity(int entity, string localKey = null, string networkKey = null)
        {
            var packed = EcsHandler.World.PackEntity(entity);
            if (!string.IsNullOrEmpty(localKey) && !_localKeyMap.ContainsKey(localKey)) _localKeyMap[localKey] = packed;
            if (!string.IsNullOrEmpty(networkKey) && !_netKeyMap.ContainsKey(networkKey)) _netKeyMap[networkKey] = packed;
            if (!_netLocalMap.ContainsKey(entity)) _netLocalMap[entity] = networkKey;
        }

        public string GetNetEntityKey(int entity)
            => _netLocalMap.TryGetValue(entity, out var key) ? key : null;

        public bool TryGetEntity(string key, out int entity)
        {
            entity = -1;
            if (string.IsNullOrEmpty(key)) return false;
            if ((_localKeyMap.TryGetValue(key, out var packed) || _netKeyMap.TryGetValue(key, out packed))
                && packed.Unpack(EcsHandler.World, out entity))
                return true;
            entity = -1;
            return false;
        }

        public bool TryGetPlayer(out int playerEntity) => TryGetEntity(Service.EntityService.PLAYER_ENTITY, out playerEntity);
        public bool TryGetPlayerEntity(out int playerEntity) => TryGetPlayer(out playerEntity);
        public bool TryGetOpponentEntity(out int opponentEntity) => TryGetEntity(Service.EntityService.OPPONENT_ENTITY, out opponentEntity);

        public void CastEvent<TEvent>(TEvent evt) where TEvent : struct
            => EcsHandler.World.GetPool<TEvent>().Add(EcsHandler.World.NewEntity()) = evt;
    }
}
