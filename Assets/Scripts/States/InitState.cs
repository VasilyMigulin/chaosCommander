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
        [Tooltip("ТЕСТ: пропустить вход через PlayFab — LoginCanvas открывается как обычно (выбор языка при " +
                 "первом старте живёт там и сохраняется), но вместо формы логина/ожидания сети сразу " +
                 "OnLoginSuccess с локальными данными (пресеты/тест-библиотека). Облако НЕ работает.")]
        public bool SkipLoginForTesting;

        public CardInstanceData[] TestingLibrary;

        [Tooltip("REVIEW-СБОРКА (для издателя): единый флаг. (1) Разблокирует ВСЮ коллекцию локально — в " +
                 "максимуме копий под лимиты колоды, чтобы сразу собирать любые колоды (пул из CardConfig). " +
                 "(2) На сервере помечает аккаунт как review (тег review_account + флаг с датой — для последующей " +
                 "чистки/удаления в Game Manager) и выдаёт 100 бустеров, чтобы издатель проверил открытие. " +
                 "Серверная часть гейтится Title Data reviewSetup=true. Бэкенд/PvP/экономика — как обычно. НЕ в прод!")]
        public bool IsReviewBuild;

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
            UIModule.Inject(this, this);   // инжектим ДО открытия: TitlePanel нужен _initContext

            // ЗАСТАВКА — только когда цикл первого захода пройден ЦЕЛИКОМ (язык + туториал + ник).
            // Иначе онбординг идёт без неё, в т.ч. при ВОЗВРАТЕ ИЗ ТУТОРИАЛА (сцена 0 стартует заново, и
            // заставка вклинилась бы между туториалом и знакомством).
            bool firstRunDone = Game.Core.Service.FirstRunFlow.LanguageChosen
                                && Game.Core.Service.FirstRunFlow.TutorialDone
                                && Game.Core.Service.FirstRunFlow.NameChosen;
            if (!firstRunDone)
            {
                Debug.Log("[InitState] первый заход не завершён → заставку пропускаем, продолжаем онбординг");
                OnContinue();
                return;
            }

            // ЗАСТАВКА первой — открываем ЯВНО по id (не полагаемся на isOpenOnInit).
            // ВАЖНО (префаб): у LoginPanel isOpenOnInit = FALSE. Иначе он откроется при Init канваса,
            // его OnOpen дёрнет TriggerLogin (автологин/скип) — и заставку не увидишь.
            // Нет TitlePanel в LoginCanvas → идём штатным пайплайном без заставки.
            if (UIModule.Get<LoginCanvas>()?.OpenPanel("TitlePanel") == null)
            {
                Debug.LogWarning("[InitState] TitlePanel не найден в LoginCanvas — заставка пропущена.");
                OnContinue();
            }
        }

        /// <summary>
        /// Стандартный пайплайн после заставки (Tap to continue). ЕДИНАЯ точка роутинга старта —
        /// раньше это делал LoginCanvas.InvokeCanvas и перебивал заставку.
        /// Порядок первого захода: язык → туториал → логин.
        /// Панели открываем ПО ID (States не реферит Core.Panel — общение через IPanelController).
        /// </summary>
        public void OnContinue()
        {
            var canvas = UIModule.Get<LoginCanvas>();

            // 1) Первый запуск: язык не выбран → панель языка (она сама продолжит на логин).
            if (!Game.Core.Service.FirstRunFlow.LanguageChosen)
            {
                Debug.Log("[InitState] первый заход: язык не выбран → LanguageSelectPanel");
                canvas?.OpenPanel("LanguageSelectPanel");
                return;
            }

            // 2) Туториал не пройден → сцена туториала.
            if (!Game.Core.Service.FirstRunFlow.TutorialDone)
            {
                Debug.Log("[InitState] первый заход: туториал не пройден → TutorialScene");
                SceneManager.LoadScene(4);   // TutorialScene
                return;
            }

            // 3) Логин: LoginPanel.OnOpen запустит автологин/скип (см. LoginPanel.TriggerLogin).
            canvas?.OpenPanel("LoginPanel");
        }

        // ── IInitStateContext ────────────────────────────────────────────────

        /// <summary>
        /// Вызывается из LoginPanel после успешной авторизации.
        /// Если TestingLibrary заполнен — загружаем тестовую коллекцию и сохраняем в облако.
        /// Если нет — загружаем библиотеку и деки из облака.
        /// </summary>
        /// <summary>Скип входа через PlayFab (тест-режим): читает LoginPanel — вместо формы логина и
        /// сетевых вызовов сразу зовёт OnLoginSuccess. Выбор языка при первом старте не затрагивается.</summary>
        public bool TestSkipLogin => SkipLoginForTesting;

        public void OnLoginSuccess()
        {
            // Скип-режим: облачные пути (BackendSession/DeckStorage) без авторизации висели бы на сети —
            // без пресета/тест-библиотеки просто уходим в меню с пустой коллекцией.
            if (SkipLoginForTesting)
            {
                Debug.LogWarning("[InitState] SkipLoginForTesting: вход в PlayFab пропущен — облако недоступно, только локальные данные.");
                if (ActivePreset() != null || (TestingLibrary != null && TestingLibrary.Length > 0))
                    LoadTestingLibrary();
                else
                {
                    Debug.LogWarning("[InitState] SkipLoginForTesting без пресета/тест-библиотеки: коллекция пустая.");
                    GoToMenu();
                }
                return;
            }

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
                // REVIEW-СБОРКА: (1) вся коллекция ПОВЕРХ реального инвентаря (после логина, до загрузки колод —
                // чтобы Prune не срезал деки); локально. (2) сервер: пометить аккаунт review + выдать 100 бустеров
                // (реальный инвентарь — проверить открытие). Фоновый вызов, вход в меню не блокирует.
                if (IsReviewBuild)
                {
                    PlayerLibrary.FillFullCollection(CardConfig);
                    ReviewService.SetupReviewAccount(
                        r => Debug.Log($"[Review] аккаунт помечен, бустеров +{(r != null ? r.BoostersGranted : 0)} ({(r != null ? r.Reason : "?")})"),
                        e => Debug.LogWarning($"[Review] setup failed: {e}"));
                }

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
