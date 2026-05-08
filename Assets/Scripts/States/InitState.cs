using AwesomeUI.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Core.DeckBuilder;
using Game.Core.Configs;
using AwesomeUI.Feature.Login;
using Game.Core.Instance;
using Game.Core.Instance.Card;
using Game.Core.Shared.Interface;

namespace Game.Core.States
{
    public class InitState : State, IInitStateContext
    {
        [Header("Testing")]
        public CardInstanceData[] TestingLibrary;

        [Header("Config")]
        public CardConfig CardConfig;

        public override void Start()
        {
            UIModule.Initialize();
            UIModule.Open<LoginCanvas>();
            UIModule.Inject(this, this);
        }

        // ── IInitStateContext ────────────────────────────────────────────────

        /// <summary>
        /// Вызывается из LoginPanel после успешной авторизации.
        /// Если TestingLibrary заполнен — загружаем тестовую коллекцию и сохраняем в облако.
        /// Если нет — загружаем библиотеку и деки из облака.
        /// </summary>
        public void OnLoginSuccess()
        {
            if (TestingLibrary != null && TestingLibrary.Length > 0)
                LoadTestingLibrary();
            else
                LoadFromCloud();
        }

        // ── Private ──────────────────────────────────────────────────────────

        void LoadTestingLibrary()
        {
            PlayerLibrary.AddInstanceCards(TestingLibrary);
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

            PlayerLibrary.LoadFromCloud(
                config: CardConfig,
                onSuccess: () =>
                {
                    DeckStorage.LoadAll(
                        onSuccess: _ => GoToMenu(),
                        onError:   err =>
                        {
                            Debug.LogWarning($"[InitState] DeckStorage load failed: {err}");
                            GoToMenu();
                        });
                },
                onError: err =>
                {
                    Debug.LogError($"[InitState] PlayerLibrary load failed: {err}");
                    GoToMenu();
                });
        }

        static void GoToMenu() => SceneManager.LoadScene(1);

        public override void Update() { } 
    }
}
