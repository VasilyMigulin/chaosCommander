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
        [SerializeField] private DefaultAbilityVfxConfig _defaultAbilityVfxConfig;

        [HideInInspector] public EcsRunHandler EcsHandler;
        [HideInInspector] public PhotonRunHandler PhotonRunHandler;

        protected Dictionary<string, EcsPackedEntity> _localKeyMap = new();
        protected Dictionary<string, EcsPackedEntity> _netKeyMap = new();
        protected Dictionary<int, string> _netLocalMap = new();
        protected Dictionary<int, (PlayerRef playerRef, NetworkPlayerData playerData)> dictionaryPlayers = new Dictionary<int, (PlayerRef, NetworkPlayerData)>();
        // PvE: Photon отсутствует — локальная симуляция считается «сервером».
        public bool IsServer => PhotonRunHandler == null || PhotonRunHandler.IsServer;
        public EcsWorld World => EcsHandler.World;

        /// <summary>Бой против ИИ без сети (см. Service.PveMode). Фиксируется на входе в бой.</summary>
        private bool _pve;

        // Идёт выход в меню: гасим сессию и грузим MenuScene. Пока true — ECS не гоняем (раннер
        // завершается асинхронно, чужие RPC/системы в этот момент бессмысленны).
        private bool _exiting;

        // RPC, отправленные из ОБРАБОТЧИКА другого RPC (OnTriggerStateInit вызывается синхронно из
        // RPC_TriggerStateInit), Fusion теряет при доставке на хост. Откладываем на следующий кадр (Update).
        private bool _pendingStateReadyRpc;

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

        /// <summary>Self-heal ресинк: снести из мап все записи КАРТ (их сущности пересозданы, старые
        /// EcsPackedEntity протухли, а AddEntity из-за ContainsKey-гарда их бы не перезаписал), сохранив
        /// записи игроков — их сущности живы. WorldResyncSystem зовёт перед пересозданием карт.</summary>
        public void ResetCardEntityMaps()
        {
            bool keepPlayer   = _localKeyMap.TryGetValue(Game.Core.Service.EntityService.PLAYER_ENTITY, out var pPacked);
            bool keepOpponent = _localKeyMap.TryGetValue(Game.Core.Service.EntityService.OPPONENT_ENTITY, out var oPacked);
            _localKeyMap.Clear();
            _netKeyMap.Clear();
            _netLocalMap.Clear();
            if (keepPlayer)   _localKeyMap[Game.Core.Service.EntityService.PLAYER_ENTITY]   = pPacked;
            if (keepOpponent) _localKeyMap[Game.Core.Service.EntityService.OPPONENT_ENTITY] = oPacked;
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
            _pve = Game.Core.Service.PveMode.Enabled;

            if (!_pve && PhotonRunHandler == null)
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
            // ── PvE: сети нет — инициализируем ECS сразу, без Photon-пайплайна/RPC. ──
            if (_pve)
            {
                UIModule.Open<BattleCanvas>();
                UIModule.Inject(this, this, EcsHandler.World, _cardConfig);
                // BattleCanvas persistent — оверлей загрузки нужно явно «поднять» заново для этого матча
                // (см. BattleLoadingOverlay.Show — без этого он навсегда остаётся невидимым после первого боя).
                FindFirstObjectByType<AwesomeUI.Feature.Battle.BattleLoadingOverlay>()?.Show();

                GameEventBus.Subscribe<CellSelectedEvent>(OnCellSelected);
                GameEventBus.Subscribe<ExitToMenuRequestedEvent>(OnExitToMenuRequested);
                GameEventBus.Subscribe<MatchEndedEvent>(OnPveMatchEnded);   // победа → отметка «уровень пройден»
                GameEventBus.Subscribe<PreStartPhaseBeginUIEvent>(OnPvePreStart);   // мулиган закрыт → интро-реплики

                try
                {
                    // Без PhotonRunHandler в инжектах: EcsCustomInject<PhotonRunHandler> останется null —
                    // системы либо проверяют null (Collect), либо имеют PvE-ветки (InitPlayer/MulliganReady).
                    EcsHandler.Init(BoardView, _cardConfig, _defaultAbilityVfxConfig);
                    Debug.Log("[BattleState][PVE] EcsHandler.Init completed (бой против ИИ).");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BattleState][PVE] EcsHandler.Init FAILED: {e}");
                }

                StartPveIntro();   // VS-экран → мулиган (в MP это делает Photon-пайплайн; без этого
                                   // BattleLoadingOverlay и мулиган-окно ждут события вечно → «бой не начинается»)
                return;
            }

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

            // Awake выходит раньше создания хендлера, если сетевой объект ещё не заспавнился (штатный случай —
            // ради него и сделан повторный поиск выше). Раньше EcsHandler так и оставался null, и Update падал
            // с NRE на EcsHandler.Run() каждый кадр. Создаём ЗДЕСЬ, а не в Awake: IsServer зависит от
            // PhotonRunHandler, и без него клиент получил бы ServerRunHandler вместо ClientRunHandler.
            if (EcsHandler == null)
            {
                Debug.Log("[BattleState] EcsHandler не создан в Awake (PhotonRunHandler тогда ещё не существовал) — создаю сейчас.");
                EcsHandler = EcsRunHandler.Create(this);
            }

            UIModule.Open<BattleCanvas>();
            UIModule.Inject(this, this, EcsHandler.World, _cardConfig);

            // Лоадинг-оверлей гасится стартом мулигана (MulliganPhaseBeginUIEvent) — при реконнекте мулигана
            // не будет, поэтому не показываем его вовсе: экран и так закроет фейд восстановления.
            if (!Game.Core.Network.ReconnectFlow.IsActive)
                FindFirstObjectByType<AwesomeUI.Feature.Battle.BattleLoadingOverlay>()?.Show();

            // Подписываемся на ECS-init триггер от сервера.
            GameEventBus.Subscribe<TriggerStateInitEvent>(OnTriggerStateInit);
            GameEventBus.Subscribe<CellSelectedEvent>(OnCellSelected);
            GameEventBus.Subscribe<ExitToMenuRequestedEvent>(OnExitToMenuRequested);

            // РЕКОННЕКТ: пайплайн хоста одноразовый (_pipelineStarted) — ни RPC_TriggerStateInit, ни ответа
            // на RPC_NotifySceneLoaded мы не дождёмся. Инициализируемся сами и стадии хоста не трогаем.
            if (Game.Core.Network.ReconnectFlow.IsActive)
            {
                Debug.Log("[BattleState] РЕКОННЕКТ: инициализирую ECS локально (пайплайн хоста уже отработал).");
                OnTriggerStateInit(default);
                return;
            }

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
                EcsHandler.Init(PhotonRunHandler, BoardView, _cardConfig, _defaultAbilityVfxConfig);
                PhotonRunHandler.RegisterGameState(this);
                Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: EcsHandler.Init completed successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] OnTriggerStateInit: EcsHandler.Init FAILED: {e}");
            }

            // ВАЖНО: RPC_NotifyStateReady и SubmitLocalCommander НЕ шлём отсюда — мы внутри обработчика
            // RPC_TriggerStateInit, а Fusion теряет RPC, отправленные из обработчика RPC (пассив: эти RPC
            // не доходили до хоста → пайплайн вис на Step 3, VS-раскрытие не запускалось). Откладываем на
            // следующий кадр (Update, вне RPC-контекста).
            // РЕКОННЕКТ: стадии пайплайна и VS-раскрытие пропускаем — бой давно идёт, а лишний
            // NotifyStateReady сбил бы счётчики готовности у хоста.
            _pendingStateReadyRpc = !Game.Core.Network.ReconnectFlow.IsActive;
        }

        // PvE: победа → локальная отметка прохождения уровня (StoryModePanel покажет «✓»).
        // MVP-хранилище — PlayerPrefs; серверный прогресс заменит его внутри PveMode.DoneKey-слоя.
        // PvE-интро: в MP VS-экран (CommandersRevealedUIEvent) и старт мулигана (MulliganPhaseBeginUIEvent)
        // публикует PhotonRunHandler (RPC_RevealCommanders → задержка → PhaseBegin). Его в PvE нет — повторяем
        // каскад локально: свой командир из активной колоды (DeckStorage), вражеский — из энкаунтера.
        // MulliganPhaseBeginUIEvent заодно гасит BattleLoadingOverlay и открывает мулиган-окно.
        private async void StartPveIntro()
        {
            const int RevealMs = 3000;   // как CommanderRevealMs в PhotonRunHandler

            string localExp = null, oppExp = null;
            int localId = -1, oppId = -1;

            var encounter = PveEncounterLocator.Current;

            // Свой командир: сюжетная колода энкаунтера (Шальной принц) ИЛИ АКТИВНАЯ колода игрока —
            // та же единая точка, что у InitDeckSystem (DeckStorage.GetActive). Раньше здесь брался
            // decks[0] (первая в списке) → при выбранной НЕ первой колоде VS-экран показывал чужого
            // командира, хотя в бою играл правильный.
            if (encounter != null && encounter.PlayerDeck != null && encounter.PlayerDeck.Commander != null)
            {
                localExp = encounter.PlayerDeck.Commander.ExpansionId;
                localId  = encounter.PlayerDeck.Commander.CardId;
            }
            else
            {
                var deck = Game.Core.DeckBuilder.DeckStorage.GetActive();
                // IsSet, а НЕ CardId != 0 — ноль валидный Id (Шальной принц), см. OwnedCardData.IsSet.
                // Строкой ниже уже правили ЭТУ же ловушку в диагностике (localId >= 0), а сам гейт остался.
                if (deck != null && deck.Commander.IsSet)
                {
                    localExp = deck.Commander.ExpansionId;
                    localId  = deck.Commander.CardId;
                }
            }

            if (encounter != null && encounter.Commander != null)
            {
                oppExp = encounter.Commander.ExpansionId;
                oppId  = encounter.Commander.CardId;
            }

            // ДИАГНОСТИКА пре-старта. localId/oppId инициализированы -1 = «не резолвнут»; >=0 = резолвнут
            // (ВКЛЮЧАЯ Id 0 — первая карта экспаншена, напр. Шальной принц). Раньше проверка была >0, из-за
            // чего командир с Id 0 не показывался в VS.
            Debug.Log($"[BattleState][PVE] пре-старт: localExp={localExp} localId={localId} | oppExp={oppExp} oppId={oppId} "
                    + $"| PlayerDeck={(encounter?.PlayerDeck != null)} cmd={(encounter?.PlayerDeck?.Commander != null)} "
                    + $"| VS покажется={(localId >= 0 && oppId >= 0)}");

            if (localId >= 0 && oppId >= 0)
            {
                GameEventBus.Publish(new CommandersRevealedUIEvent
                {
                    LocalExpansionId    = localExp,
                    LocalCardId         = localId,
                    OpponentExpansionId = oppExp,
                    OpponentCardId      = oppId,
                });
                await System.Threading.Tasks.Task.Delay(RevealMs);
                if (_exiting || this == null) return;   // вышли из боя, пока крутился VS-экран
            }

            GameEventBus.Publish(new MulliganPhaseBeginUIEvent());
            Debug.Log("[BattleState][PVE] интро завершено → мулиган");
        }

        private void OnPveMatchEnded(MatchEndedEvent evt)
        {
            // Реплики исхода (говорящая голова) — победные/поражение из энкаунтера.
            var enc = PveEncounterLocator.Current;
            if (enc != null)
                PublishStoryLines(evt.LocalResult == MatchResult.Win ? enc.VictoryLines : enc.DefeatLines);

            if (evt.LocalResult != MatchResult.Win) return;

            // Награда за ПЕРВОЕ прохождение (стори-прогресс = источник стартовых карт): проверяем флаг
            // ДО записи — повторные победы карт не дают.
            bool firstWin = PlayerPrefs.GetInt(Game.Core.Service.PveMode.CurrentDoneKey(), 0) == 0;
            PlayerPrefs.SetInt(Game.Core.Service.PveMode.CurrentDoneKey(), 1);
            PlayerPrefs.Save();
            Debug.Log($"[BattleState][PVE] уровень '{Game.Core.Service.PveMode.CurrentDoneKey()}' пройден ✓");

            if (firstWin && enc != null && enc.RewardCards != null)
                GrantEncounterRewards(enc);
        }

        // Выдача карт-наград в библиотеку игрока + тосты «получена карта» + сохранение в облако.
        // MVP: легаси-путь PlayerLibrary/SaveToCloud; при переходе на Economy v2 заменить на серверный грант.
        private static void GrantEncounterRewards(PveEncounterConfig enc)
        {
            bool granted = false;
            foreach (var reward in enc.RewardCards)
            {
                if (reward.Card == null || reward.Card.CardData == null) continue;
                int count = Mathf.Max(1, reward.Count);

                Game.Core.DeckBuilder.PlayerLibrary.AddCard(reward.Card.CardData, count);
                granted = true;

                GameEventBus.Publish(new RewardCardsGrantedUIEvent
                {
                    Visual = Game.Core.Instance.Card.CardVisualDataFactory.From(reward.Card.CardData),
                    Count  = count,
                });
                Debug.Log($"[BattleState][PVE] награда: {count}× '{reward.Card.name}' → библиотека");
            }

            if (granted)
                Game.Core.DeckBuilder.PlayerLibrary.SaveToCloud(
                    onError: err => Debug.LogWarning($"[BattleState][PVE] не удалось сохранить награды в облако: {err}"));
        }

        // Мулиган закрыт, борд виден → интро-реплики боя (один раз).
        private bool _introLinesShown;
        private void OnPvePreStart(PreStartPhaseBeginUIEvent _)
        {
            if (_introLinesShown) return;
            _introLinesShown = true;
            var enc = PveEncounterLocator.Current;
            if (enc != null) PublishStoryLines(enc.IntroLines);
        }

        private static void PublishStoryLines(PveEncounterConfig.StoryLine[] lines)
        {
            if (lines == null) return;
            foreach (var line in lines)
            {
                if (line.Portrait == null && string.IsNullOrEmpty(line.TextKey) && string.IsNullOrEmpty(line.FallbackText))
                    continue;   // пустая строка конфига
                GameEventBus.Publish(new StoryLineUIEvent
                {
                    Portrait     = line.Portrait,
                    SpeakerKey   = line.SpeakerKey,
                    TextKey      = line.TextKey,
                    FallbackText = line.FallbackText,
                    Duration     = line.Duration <= 0f ? 3f : line.Duration,
                });
            }
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
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnPveMatchEnded);
            GameEventBus.Unsubscribe<PreStartPhaseBeginUIEvent>(OnPvePreStart);
            EcsHandler?.Dispose();
            MatchTracker.Shutdown();
            Game.Core.Service.PveMode.Reset();   // не тащим PvE-флаг в следующий (возможно MP) матч
            Game.Core.Service.MatchIdentity.Clear();   // и идентичность матча (отчёт уже ушёл в MatchEndedEvent)
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
                // PvE: сессии нет — гасить нечего.
                if (!_pve && PhotonInitializer.Instance != null)
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

            // Отложенная отправка RPC вне RPC-контекста (см. OnTriggerStateInit): на пассиве только так
            // они реально доходят до хоста.
            if (_pendingStateReadyRpc)
            {
                _pendingStateReadyRpc = false;
                Debug.Log($"[BattleState][{(PhotonRunHandler.IsServer ? "HOST" : "CLIENT")}] Deferred: SubmitLocalCommander + RPC_NotifyStateReady (вне RPC-контекста).");
                PhotonRunHandler.SubmitLocalCommander();
                PhotonRunHandler.RPC_NotifyStateReady();
            }

            // ECS не поднялся (напр. PhotonRunHandler так и не заспавнился) — молчать нельзя, но и сыпать
            // NRE каждый кадр тоже: говорим ОДИН раз, что именно сломано, и не гоняем пайплайн.
            if (EcsHandler == null)
            {
                if (!_noEcsLogged)
                {
                    _noEcsLogged = true;
                    Debug.LogError("[BattleState] EcsHandler == null — бой не инициализирован (PhotonRunHandler не найден?). Пайплайн ECS не запускается.");
                }
                return;
            }

            EcsHandler.Run();
        }

        bool _noEcsLogged;

        public void FixedUpdate() { if (EcsHandler != null) EcsHandler.FixedRun(); }
        public void LateUpdate()  { if (EcsHandler != null) EcsHandler.LateRun(); }

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