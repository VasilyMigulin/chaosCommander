using Game.Core.Ecs.Components;
using Game.Core.Ecs.Handlers;
using Game.Core.Model.Card;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Game.Core.View;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// MonoBehaviour, который создаёт и тикает EcsRunHandler.
    /// Должен быть на сцене после загрузки игровой сцены.
    /// Реализует IGameStateContext для передачи в ECS.
    /// </summary>
    public class GameInitializer : MonoBehaviour, IGameStateContext
    {
        [Header("Photon")]
        [SerializeField] private PhotonRunHandler _photonRunHandler;

        [Header("Input")]
        [SerializeField] private BoardInputController _boardInputController;
        [SerializeField] private int _localPlayerIndex;

        [Header("Deck settings")]
        [SerializeField] private int _shuffleSeed = 42;

        public bool IsServer => _photonRunHandler != null && _photonRunHandler.IsServer;

        private EcsRunHandler _ecsHandler;

        private void Start()
        {
            InitEcs();
        }

        private void InitEcs()
        {
            _ecsHandler = EcsRunHandler.Create(this);

            // ── Создаём модельный слой ─────────────────────────────────────
            var cardRegistry = CardRegistry.CreateDefault();

            var deckManager  = new DeckManager(2);
            var deckTemplate = new int[] { 0,0,0,0, 1,1,1, 2,2, 3,3,3,3, 6,6,6,6, 4,4,4 };
            deckManager.SetDeck(0, deckTemplate, _shuffleSeed);
            deckManager.SetDeck(1, deckTemplate, _shuffleSeed + 1);

            var castService = new CardCastService(_ecsHandler.World, cardRegistry, targetSelector: null);

            // Контекст локального игрока и мосты делегирования
            var localCtx   = new LocalPlayerContext(_localPlayerIndex);
            var endTurnReq = _photonRunHandler as IEndTurnRequester;

            // Передаём зависимости в ECS через Inject (LeoECS DI)
            _ecsHandler.Init(cardRegistry, deckManager, localCtx, endTurnReq);

            // Даём PhotonRunHandler ссылки
            if (_photonRunHandler != null)
            {
                _photonRunHandler.SetEcsHandler(_ecsHandler);
                _photonRunHandler.SetCardCastService(castService);
                _photonRunHandler.SetDeckManager(deckManager);
            }


            _eventBus = new GameEventBus();
            _eventBus.Init(_ecsHandler.World);
            Events = _eventBus;

            // Инициализируем BoardInputController
            if (_boardInputController != null)
                _boardInputController.Init(_ecsHandler.World, _localPlayerIndex);

            // Создаём singleton сущность состояния хода
            int turnEntity = _ecsHandler.World.NewEntity();
            ref var turnState = ref _ecsHandler.World.GetPool<TurnStateComponent>().Add(turnEntity);
            turnState.CurrentPlayerIndex = 0;
            turnState.TimeRemaining      = 60f;
            turnState.TurnNumber         = 1;
            turnState.IsActive           = false;

            // Создаём сущности игроков
            CreatePlayer(0, "Player0", startGold: 3, goldPerTurn: 2, maxHealth: 30, maxMana: 10);
            CreatePlayer(1, "Player1", startGold: 3, goldPerTurn: 2, maxHealth: 30, maxMana: 10);

            // Создаём базы игроков
            CreateBase(0, maxHealth: 20);
            CreateBase(1, maxHealth: 20);

            // Статистика матча
            CreateMatchStats(0);
            CreateMatchStats(1);

            Debug.Log($"[GameInitializer] ECS initialized. IsServer={IsServer}");

            // Сервер стартует первый ход и раздаёт начальные карты
            if (IsServer)
            {
                int startEvt = _ecsHandler.World.NewEntity();
                ref var started = ref _ecsHandler.World.GetPool<TurnStartedEvent>().Add(startEvt);
                started.PlayerIndex = 0;
                started.TurnNumber  = 1;

                ref var turn = ref _ecsHandler.World.GetPool<TurnStateComponent>().Get(turnEntity);
                turn.IsActive = true;

                if (_photonRunHandler != null)
                {
                    _photonRunHandler.RPC_DrawCards(0, 3);
                    _photonRunHandler.RPC_DrawCards(1, 3);
                }
            }
        }

        private void Update()
        {
            _ecsHandler?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _ecsHandler?.Dispose();
        }

        private void CreateBase
        {
            int entity = _ecsHandler.World.NewEntity();
            _ecsHandler.World.GetPool<BaseComponent>().Add(entity) = new BaseComponent
            {
                OwnerPlayerIndex = playerIndex,
                Health           = maxHealth,
                MaxHealth        = maxHealth,
                IsDestroyed      = false
            };
        }

        private void CreatePlayer
        {
            int entity = _ecsHandler.World.NewEntity();

            ref var player = ref _ecsHandler.World.GetPool<PlayerComponent>().Add(entity);
            player.PlayerIndex = playerIndex;
            player.Health = maxHealth;
            player.MaxHealth = maxHealth;
            player.Gold = startGold;
            player.Mana = 0;

            ref var resources = ref _ecsHandler.World.GetPool<PlayerResourcesComponent>().Add(entity);
            resources.GoldPerTurn = goldPerTurn;
            resources.MaxMana = maxMana;
        }

        private void CreateMatchStats(int playerIndex)
        {
            int entity = _ecsHandler.World.NewEntity();
            ref var stats = ref _ecsHandler.World.GetPool<MatchStatsComponent>().Add(entity);
            stats.PlayerIndex = playerIndex;
            stats.TotalDamageDealt = 0;
            stats.CreaturesKilled = 0;
            stats.TurnsPlayed = 0;
        }

        private void Update()
        {
            _ecsHandler?.Run();
        }

        private void FixedUpdate()
        {
            _ecsHandler?.FixedRun();
        }

        private void LateUpdate()
        {
            _ecsHandler?.LateRun();
        }

        private void OnDestroy()
        {
            _ecsHandler?.Dispose();
            _ecsHandler = null;
        }
    }
}
