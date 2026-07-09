using AwesomeUI.Core;
using AwesomeUI.Feature.Login;
using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Instance;
using Game.Core.Instance.Card;
using Game.Core.Shared.Interface;
using Game.Core.Model.Card;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core.States
{
    public class InitState : State, IInitStateContext
    {
        [Header("Testing")]
        public CardInstanceData[] TestingLibrary;

        [Tooltip("Пресеты тест-колод (Create → Game → Deck Preset): собери несколько и переключай индексом ниже. " +
                 "Библиотека автоматически пополняется картами активного пресета.")]
        public DeckPreset[] DeckPresets;
        [Tooltip("Какой пресет из DeckPresets использовать. -1 = пресеты ВЫКЛЮЧЕНЫ: обычная загрузка " +
                 "из облака — играют колоды, собранные в дек-билдере.")]
        public int ActiveDeckPreset = 0;

        [Tooltip("ЛЕГАСИ: старый формат (элемент = копия, [0] = командир). Используется, только если пресет не задан.")]
        public CardInstanceData[] TestingDeck;

        [Header("Config")]
        public CardConfig CardConfig;

        private DeckBuilderService deckService = new DeckBuilderService();

        public override void Start()
        {
            UIModule.Initialize();
            UIModule.Open<LoginCanvas>();
            UIModule.Inject(this, this);

            // ЦИКЛ ПЕРВОГО ЗАХОДА: язык → туториал (сцена 4) → логин. Стейт НЕ знает панелей (слои!):
            // выбор языка при первом запуске открывает сам UI (LoginPanel.OnInject → LanguageSelectPanel,
            // связь через Service.FirstRunFlow). Здесь — только сценный роутинг в туториал.
            if (Game.Core.Service.FirstRunFlow.LanguageChosen && !Game.Core.Service.FirstRunFlow.TutorialDone)
            {
                Debug.Log("[InitState] первый заход: туториал не пройден → TutorialScene");
                SceneManager.LoadScene(4);   // TutorialScene
            }
        }

        // ── IInitStateContext ────────────────────────────────────────────────

        /// <summary>
        /// Вызывается из LoginPanel после успешной авторизации.
        /// Если TestingLibrary заполнен — загружаем тестовую коллекцию и сохраняем в облако.
        /// Если нет — загружаем библиотеку и деки из облака.
        /// </summary>
        public void OnLoginSuccess()
        {
            // Тест-режим: задан пресет колоды ИЛИ тест-библиотека (пресета достаточно —
            // библиотека автоматически пополнится его картами).
            if (ActivePreset() != null || (TestingLibrary != null && TestingLibrary.Length > 0))
                LoadTestingLibrary();
            else
                LoadFromCloud();
        }

        DeckPreset ActivePreset()
        {
            if (ActiveDeckPreset < 0) return null;   // -1 = пресеты выключены (играем собранными колодами)
            if (DeckPresets == null || DeckPresets.Length == 0) return null;
            int i = Mathf.Clamp(ActiveDeckPreset, 0, DeckPresets.Length - 1);
            return DeckPresets[i];
        }

        void LoadTestingDeck()
        {
            var preset = ActivePreset();
            if (preset != null)
            {
                // Пресет-ассет: командир + карты с количеством (без дублей-элементов).
                if (preset.Commander != null)
                    deckService.TrySetCommander(preset.Commander.CardData);
                foreach (var entry in preset.Cards)
                {
                    if (entry.Card == null) continue;
                    int copies = Mathf.Max(1, entry.Count);
                    for (int c = 0; c < copies; c++)
                    {
                        var result = deckService.TryAdd(entry.Card.CardData, false);
                        if (result != DeckBuilderService.AddResult.Ok)
                            Debug.LogWarning($"[InitState] пресет '{preset.name}': '{entry.Card.name}' " +
                                             $"(копия {c + 1}/{copies}) отклонена: {result}");
                    }
                }
                var savedPreset = deckService.Export(string.IsNullOrEmpty(preset.DeckName) ? preset.name : preset.DeckName);
                DeckStorage.SetTesting(savedPreset);
                Debug.Log($"[InitState] тест-колода из пресета '{preset.name}' ({preset.DeckName})");
                return;
            }

            // ЛЕГАСИ-путь: массив, [0] = командир, каждая копия отдельным элементом.
            deckService.TrySetCommander(TestingDeck[0].CardData);

            for (int i = 1; i < TestingDeck.Length; i++)
            {
                deckService.TryAdd(TestingDeck[i].CardData, false);
            }

            var saved = deckService.Export("TestDeck");
            DeckStorage.SetTesting(saved);
        }

        // ── Private ──────────────────────────────────────────────────────────

        void LoadTestingLibrary()
        {
            if (TestingLibrary != null && TestingLibrary.Length > 0)
                PlayerLibrary.AddInstanceCards(TestingLibrary);

            // Карты активного пресета — тоже в библиотеку (пресета достаточно без TestingLibrary).
            var preset = ActivePreset();
            if (preset != null)
                PlayerLibrary.AddInstanceCards(System.Linq.Enumerable.ToArray(preset.AllCards()));

            if (preset != null || (TestingDeck != null && TestingDeck.Length > 0))
                LoadTestingDeck();

            GoToMenu();
        }

        void LoadFromCloud()
        {
            if (CardConfig == null)
            {
                Debug.LogError("[InitState] CardConfig is not assigned — cannot load library from cloud.");
                GoToMenu();
                return;
            }

            // Бэкенд-инициализация: серверное время → миграция → инвентарь (PlayerLibrary + PlayerWallet).
            // Затем грузим колоды. Любой сбой не блокирует вход в меню (см. BackendSession).
            BackendSession.Initialize(CardConfig, onDone: () =>
            {
                DeckStorage.LoadAll(
                    onSuccess: _ => GoToMenu(),
                    onError:   err =>
                    {
                        Debug.LogWarning($"[InitState] DeckStorage load failed: {err}");
                        GoToMenu();
                    });
            });
        }

        static void GoToMenu() => SceneManager.LoadScene(1);

        public override void Update() { } 
    }
}
