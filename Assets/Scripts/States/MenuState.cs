using AwesomeUI.Core;
using AwesomeUI.Feature;
using UnityEngine;
using Game.Core.Shared.Interface;
using Game.Core.Photon;
using System;
using System.Threading.Tasks;

namespace Game.Core.States
{
    public class MenuState : State, IMenuStateContext
    {
        Task sessionTask;

        public event Action<MatchmakingUiStatus> MatchmakingStatusChanged;

        public override void Awake()
        {
            base.Awake();

            PhotonInitializer.Initialize();
        }

        public override void Start()
        {
            UIModule.Open<MainMenuCanvas>();
            UIModule.Inject(this, this);
            // Дев-кнопка «PvE» (LogCopyOverlay, сборка Mono — интерфейс ей недоступен) просит PvE через шину.
            Events.GameEventBus.Subscribe<Events.PveStartRequestedEvent>(OnPveStartRequested);
        }

        public override void OnDestroy()
        {
            Events.GameEventBus.Unsubscribe<Events.PveStartRequestedEvent>(OnPveStartRequested);
            base.OnDestroy();
        }

        void OnPveStartRequested(Events.PveStartRequestedEvent e) => StartPveBattle(e.EncounterPath);

        /// <summary>
        /// Старт PvE: сперва ЧИСТО гасим активный матчмейкинг (LoadScene посреди джоина убивал раннер →
        /// асинхронный фейл поиска заново открывал меню-канвас ПОВЕРХ боевого UI), затем PveMode + BattleScene.
        /// </summary>
        public async void StartPveBattle(string encounterPath = null)
        {
            var mm = PhotonInitializer.Instance?.Matchmaking;
            if (mm != null && mm.CurrentState != MatchmakingState.Idle)
            {
                Debug.Log("[MenuState] PvE: активен матчмейкинг — отменяю перед стартом боя…");
                mm.OnStateChanged -= OnMatchmakingStateChanged;   // фейл/отмена не должны дёргать меню-UI
                await mm.CancelMatchmaking();
                MatchmakingUiHub.Reset();
            }

            if (!string.IsNullOrEmpty(encounterPath))
                Game.Core.Service.PveMode.EncounterPath = encounterPath;
            Game.Core.Service.PveMode.Enabled = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene(3);   // BattleScene
        }

        public void StartMatchMaking()
        {
            var mm = PhotonInitializer.Instance.Matchmaking;
            // Транслируем состояния Photon-матчмейкинга в UI-статус для FindOpponentPanel.
            mm.OnStateChanged -= OnMatchmakingStateChanged;
            mm.OnStateChanged += OnMatchmakingStateChanged;

            // Персистентный хаб: статус и cancel переживут гибель MenuState при загрузке LobbyScene.
            MatchmakingUiHub.CancelHandler = CancelMatchmakingAndReturn;
            MatchmakingUiHub.SetStatus(MatchmakingUiStatus.Searching);

            sessionTask = mm.FindMatchAsync();
        }

        public void CancelMatchMaking() => MatchmakingUiHub.Cancel();

        // Статик (переживает гибель MenuState): завершаем сессию и возвращаемся в меню.
        static async void CancelMatchmakingAndReturn()
        {
            var mm = PhotonInitializer.Instance?.Matchmaking;
            if (mm != null)
                await mm.CancelMatchmaking();

            MatchmakingUiHub.Reset();
            UnityEngine.SceneManagement.SceneManager.LoadScene(1);   // MenuScene
        }

        void OnMatchmakingStateChanged(MatchmakingState state)
        {
            // Idle приходит после Ready (успех) и после Failed/Cancelled — не переопределяем им статус.
            if (state == MatchmakingState.Idle) return;

            MatchmakingUiStatus ui = state switch
            {
                MatchmakingState.SearchingSessions
                    or MatchmakingState.Joining
                    or MatchmakingState.WaitingForPlayers => MatchmakingUiStatus.Searching,
                MatchmakingState.Ready     => MatchmakingUiStatus.OpponentFound,
                MatchmakingState.Failed    => MatchmakingUiStatus.Failed,
                MatchmakingState.Cancelled => MatchmakingUiStatus.Cancelled,
                _                          => MatchmakingUiStatus.Searching,
            };

            MatchmakingStatusChanged?.Invoke(ui);
            MatchmakingUiHub.SetStatus(ui);   // персистентный канал для UI (переживает смену сцены)
        }

        public override void Update()
        {

        }
    }
}
