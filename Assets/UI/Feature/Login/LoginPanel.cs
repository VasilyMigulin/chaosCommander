using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using Game.Core.DeckBuilder;
using Game.Core.Service;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Login
{
    /// <summary>
    /// Вход + знакомство. Флоу (после языка/туториала):
    ///   1) ТИХИЙ гостевой вход (CustomId устройства) — игрок видит только спиннер; при сбое сети ретрай + offline-экран.
    ///   2) ПЕРВЫЙ запуск (FirstRunFlow.NameChosen == false) → панель «Давай знакомиться»: ник (displayName)
    ///      + «Продолжить» + кнопки Google/Apple (выключены до релиза). «Продолжить» → SetDisplayName → меню.
    ///   3) ПОВТОРНЫЙ запуск (ник уже задан) → сразу в меню.
    /// Email/пароль/регистрация из флоу выпилены — базовая личность гость, соцвход добавится привязкой.
    ///
    /// Префаб (на LoginCanvas), isOpenOnInit = FALSE. Три взаимоисключающих состояния — код сам их
    /// переключает, в префабе все три можно оставить выключенными:
    ///   LoginPanel
    ///     ├─ LoadingOverlay  (GameObject) ← _loadingOverlay   (спиннер, поверх состояний)
    ///     ├─ ConnectRoot     (GameObject) ← _connectRoot      (1) «подключаемся» на время входа
    ///     │    └─ ConnectText   (TMP_Text)       ← _connectText    (крутящиеся шутки ui.login.flavor.*)
    ///     ├─ WelcomeRoot     (GameObject) ← _welcomeRoot      (2) «Давай знакомиться» (только 1-й запуск)
    ///     │    ├─ NicknameInput (TMP_InputField) ← _nicknameInput
    ///     │    ├─ ContinueButton(Button)         ← _continueButton
    ///     │    ├─ GoogleButton  (Button)         ← _googleButton   (выключается кодом)
    ///     │    ├─ AppleButton   (Button)         ← _appleButton    (выключается кодом)
    ///     │    └─ SoonNote      (GameObject)     ← _socialNote     («скоро», опц.)
    ///     ├─ OfflineRoot     (GameObject) ← _offlineRoot      (3) нет связи, попытки исчерпаны
    ///     │    └─ RetryButton   (Button)         ← _retryButton
    ///     └─ FeedbackText    (TMP_Text)   ← _feedbackText     (ошибки: ник занят / нет связи)
    /// </summary>
    public class LoginPanel : SourcePanel
    {
        [Header("Loading / offline")]
        [SerializeField] GameObject _loadingOverlay;
        [Tooltip("Экран «сервер недоступен» (все попытки входа исчерпаны). Необязательно.")]
        [SerializeField] GameObject _offlineRoot;
        [Tooltip("Кнопка «Повторить» на offline-экране.")]
        [SerializeField] Button     _retryButton;
        [SerializeField] TMP_Text   _feedbackText;

        [Header("Подключение (шуточные фразы, пока идёт вход)")]
        [Tooltip("Экран «подключаемся к серверу» — показывается на время гостевого входа.")]
        [SerializeField] GameObject _connectRoot;
        [Tooltip("Строка с крутящимися шуточными фразами.")]
        [SerializeField] TMP_Text   _connectText;
        [SerializeField] float      _flavorInterval = 2.2f;
        [SerializeField] float      _flavorFade = 0.3f;

        [Header("Знакомство (первый запуск)")]
        [Tooltip("Корень панели «Давай знакомиться» — показывается после гостевого входа при первом запуске.")]
        [SerializeField] GameObject     _welcomeRoot;
        [SerializeField] TMP_InputField _nicknameInput;
        [SerializeField] Button         _continueButton;
        [Tooltip("Соцвход — к релизу. Сейчас выключены (SocialEnabled=false).")]
        [SerializeField] Button         _googleButton;
        [SerializeField] Button         _appleButton;
        [Tooltip("Плашка «скоро» над соц-кнопками — показывается, пока соцвход выключен. Опц.")]
        [SerializeField] GameObject     _socialNote;

        /// <summary>Вызывается после успешного входа (для внешних подписчиков).</summary>
        public event Action OnLoginSuccess;

        // Соцвход включим на релизе (нужна нативная обвязка токенов Google/Apple — см. PlayFabService).
        const bool  SocialEnabled   = false;
        const int   MinNick         = 3;    // ограничения PlayFab UpdateUserTitleDisplayName
        const int   MaxNick         = 25;
        const int   MaxAutoRetries  = 3;    // авто-повторы гостевого входа при транзиентном сбое
        const float RetryDelay      = 2f;

        bool _isLoggingIn;
        bool _busy;          // защита кнопки «Продолжить» от даблклика
        int  _loginAttempts;
        bool _skippedLogin;  // тест-скип отработал

        // Шуточные фразы подключения (как в поиске соперника). Ключ + RU-фолбэк; перевод в card_text.csv.
        static readonly (string Key, string Ru)[] ConnectPhrases =
        {
            ("ui.login.flavor.01", "Ищем свободные места…"),
            ("ui.login.flavor.02", "Стучимся в сервер…"),
            ("ui.login.flavor.03", "Будим админа…"),
            ("ui.login.flavor.04", "Пробиваемся через очередь…"),
            ("ui.login.flavor.05", "Протираем пыль с базы данных…"),
            ("ui.login.flavor.06", "Пересчитываем ваши богатства…"),
            ("ui.login.flavor.07", "Проверяем, не сбежал ли прогресс…"),
            ("ui.login.flavor.08", "Ищем ваш аккаунт в стопке…"),
            ("ui.login.flavor.09", "Уговариваем сервер пустить…"),
            ("ui.login.flavor.10", "Раскладываем карты по местам…"),
            ("ui.login.flavor.11", "Ищем розетку для сервера…"),
            ("ui.login.flavor.12", "Договариваемся с облаком…"),
        };

        // Крутёж фраз через Update (без корутин: панель инжектится ещё неактивной).
        enum Phase { Hold, FadeOut, FadeIn }
        CanvasGroup _connectCg;
        Phase _phase;
        float _timer;
        int   _flavorIndex = -1;
        bool  _connecting;

        [UIInject] IInitStateContext _initContext;

        public override void OnInject()
        {
            base.OnInject();
            Unject();   // защита от повторного OnInject

            _continueButton?.onClick.AddListener(OnContinueClicked);
            _retryButton?.onClick.AddListener(StartGuestLogin);
            _googleButton?.onClick.AddListener(OnGoogleClicked);
            _appleButton?.onClick.AddListener(OnAppleClicked);

            // Соцвход выключен до релиза: гасим кнопки, показываем «скоро».
            if (_googleButton != null) _googleButton.interactable = SocialEnabled;
            if (_appleButton != null) _appleButton.interactable = SocialEnabled;
            if (_socialNote != null) _socialNote.SetActive(!SocialEnabled);

            SetLoading(false);
            if (_connectRoot != null) _connectRoot.SetActive(false);
            if (_welcomeRoot != null) _welcomeRoot.SetActive(false);
            if (_offlineRoot != null) _offlineRoot.SetActive(false);
            ClearFeedback();

            // Логин НЕ стартуем в OnInject: канвас инжектит ВСЕ панели на старте (в т.ч. под заставкой).
            // Автологин запускается в OnOpen (когда панель реально показана).
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            TriggerLogin();
        }

        // ── Гостевой вход ───────────────────────────────────────────────────────

        void TriggerLogin()
        {
            // Язык не выбран (первый старт) → ждём: LanguageSelectPanel потом снова откроет LoginPanel.
            if (!FirstRunFlow.LanguageChosen) return;
            if (TrySkipLogin()) return;     // тест-скип: без сети
            StartGuestLogin();
        }

        // ТЕСТ-СКИП (InitState.SkipLoginForTesting): без сети сразу переходим к знакомству/меню.
        bool TrySkipLogin()
        {
            if (_initContext == null || !_initContext.TestSkipLogin) return false;
            if (_skippedLogin) return true;
            _skippedLogin = true;
            Debug.LogWarning("[LoginPanel] TestSkipLogin: вход через PlayFab ПРОПУЩЕН (тест-режим)");
            AfterLogin();
            return true;
        }

        void StartGuestLogin()
        {
            if (_isLoggingIn) return;
            _loginAttempts = 0;
            AttemptGuestLogin();
        }

        void AttemptGuestLogin()
        {
            _isLoggingIn = true;
            SetLoading(true);
            BeginConnect();          // экран «подключаемся» с шуточными фразами
            ClearFeedback();
            if (_welcomeRoot != null) _welcomeRoot.SetActive(false);
            if (_offlineRoot != null) _offlineRoot.SetActive(false);

            PlayFabService.LoginWithCustomId(SystemInfo.deviceUniqueIdentifier,
                onSuccess: () => { _isLoggingIn = false; AfterLogin(); },
                onError:   OnGuestLoginError);
        }

        void OnGuestLoginError(string error)
        {
            _isLoggingIn = false;
            _loginAttempts++;
            Debug.LogWarning($"[LoginPanel] Гостевой вход не удался (попытка {_loginAttempts}/{MaxAutoRetries + 1}): {error}");

            if (_loginAttempts <= MaxAutoRetries) { StartCoroutine(RetryGuestLogin()); return; }
            ShowOffline();
        }

        IEnumerator RetryGuestLogin()
        {
            yield return new WaitForSeconds(RetryDelay);
            AttemptGuestLogin();
        }

        // ── После входа: знакомство или сразу меню ───────────────────────────────

        void AfterLogin()
        {
            SetLoading(false);
            if (_offlineRoot != null) _offlineRoot.SetActive(false);

            // Первый запуск И собрана панель знакомства → «Давай знакомиться»; иначе (ник задан ИЛИ панели нет
            // в префабе) — сразу в меню. Гейт по _welcomeRoot != null: без собранного UI не блокируем вход.
            if (!FirstRunFlow.NameChosen && _welcomeRoot != null)
            {
                EndConnect();     // отдаём экран знакомству
                ShowWelcome();
            }
            else
            {
                Complete();       // экран подключения НЕ гасим — впереди загрузка данных (см. Complete)
            }
        }

        void ShowWelcome()
        {
            if (_welcomeRoot != null) _welcomeRoot.SetActive(true);
            if (_nicknameInput != null) _nicknameInput.text = "";
            ClearFeedback();
        }

        void OnContinueClicked()
        {
            if (_busy) return;
            string nick = _nicknameInput != null ? _nicknameInput.text.Trim() : "";
            if (nick.Length < MinNick)
            {
                ShowFeedback(T("ui.welcome.name_too_short", $"Ник слишком короткий (мин. {MinNick})"));
                return;
            }
            if (nick.Length > MaxNick) nick = nick.Substring(0, MaxNick);

            _busy = true;
            SetLoading(true);

            // Тест-скип: без сети — просто запоминаем и идём в меню.
            if (_initContext != null && _initContext.TestSkipLogin)
            {
                FirstRunFlow.NameChosen = true;
                _busy = false;
                Complete();
                return;
            }

            PlayFabService.SetDisplayName(nick,
                onSuccess: () => { FirstRunFlow.NameChosen = true; _busy = false; Complete(); },
                onError:   err =>
                {
                    _busy = false;
                    SetLoading(false);
                    ShowFeedback(TranslateNameError(err));
                });
        }

        void Complete()
        {
            if (_welcomeRoot != null) _welcomeRoot.SetActive(false);

            // Дальше идёт загрузка инвентаря и колод (BackendSession + DeckStorage) — секунда-две, в течение
            // которых сцена ещё не сменилась. Держим на экране «подключение», иначе игрок видит пустую панель.
            if (!_connecting) BeginConnect();

            OnLoginSuccess?.Invoke();
            if (_initContext != null) _initContext.OnLoginSuccess();
            else SceneManager.LoadScene(1);
        }

        // ── Соцвход (заглушки до релиза) ─────────────────────────────────────────
        // Кнопки выключены (interactable=false), поэтому клики сюда не доходят. Оставлено готовым к релизу:
        // нужна нативная часть — получить serverAuthCode (Google) / identityToken (Apple) и передать в Link*.
        void OnGoogleClicked() { if (SocialEnabled) { /* PlayFabService.LinkGoogleAccount(code, ...) */ } }
        void OnAppleClicked()  { if (SocialEnabled) { /* PlayFabService.LinkApple(token, ...) */ } }

        // ── Offline ──────────────────────────────────────────────────────────────

        void ShowOffline()
        {
            SetLoading(false);
            EndConnect();
            if (_welcomeRoot != null) _welcomeRoot.SetActive(false);
            ShowFeedback(OfflineMessage());
            if (_offlineRoot != null) _offlineRoot.SetActive(true);
            if (_retryButton != null) _retryButton.gameObject.SetActive(true);
        }

        static string OfflineMessage()
        {
            var loc = T("ui.login.offline", "Сервер недоступен. Проверьте подключение к интернету.");
            // Устройство на русском → почти наверняка РФ, где PlayFab без VPN не отвечает: добавляем подсказку.
            if (Application.systemLanguage == SystemLanguage.Russian)
                loc += " " + T("ui.login.offline_vpn", "Проверьте, работает ли VPN.");
            return loc;
        }

        // ── Экран подключения: крутёж шуточных фраз ───────────────────────────────

        void BeginConnect()
        {
            if (_connectRoot != null) _connectRoot.SetActive(true);
            if (_connectCg == null) _connectCg = EnsureGroup(_connectText);
            _connecting = true;
            _phase = Phase.Hold;
            _timer = 0f;
            ShowNextPhrase();
            if (_connectCg != null) _connectCg.alpha = 1f;
        }

        void EndConnect()
        {
            _connecting = false;
            if (_connectRoot != null) _connectRoot.SetActive(false);
        }

        void Update()
        {
            if (!_connecting || _connectCg == null) return;
            _timer += Time.unscaledDeltaTime;

            switch (_phase)
            {
                case Phase.Hold:
                    if (_timer >= _flavorInterval) { _timer = 0f; _phase = Phase.FadeOut; }
                    break;
                case Phase.FadeOut:
                    _connectCg.alpha = Mathf.Clamp01(1f - _timer / _flavorFade);
                    if (_timer >= _flavorFade) { _timer = 0f; ShowNextPhrase(); _phase = Phase.FadeIn; }
                    break;
                case Phase.FadeIn:
                    _connectCg.alpha = Mathf.Clamp01(_timer / _flavorFade);
                    if (_timer >= _flavorFade) { _timer = 0f; _connectCg.alpha = 1f; _phase = Phase.Hold; }
                    break;
            }
        }

        void ShowNextPhrase()
        {
            if (_connectText == null || ConnectPhrases.Length == 0) return;
            int next = UnityEngine.Random.Range(0, ConnectPhrases.Length);
            if (ConnectPhrases.Length > 1 && next == _flavorIndex) next = (next + 1) % ConnectPhrases.Length;
            _flavorIndex = next;
            _connectText.text = T(ConnectPhrases[next].Key, ConnectPhrases[next].Ru);
        }

        static CanvasGroup EnsureGroup(Component c)
        {
            if (c == null) return null;
            var cg = c.GetComponent<CanvasGroup>();
            return cg != null ? cg : c.gameObject.AddComponent<CanvasGroup>();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static string T(string key, string ru) => CardTextLocalization.GetText(key, ru);

        // PlayFab возвращает англоязычные коды; занятое/запрещённое имя → NameNotAvailable.
        static string TranslateNameError(string error)
        {
            if (!string.IsNullOrEmpty(error) &&
                (error.IndexOf("NameNotAvailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 error.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0))
                return T("ui.welcome.name_taken", "Это имя недоступно — попробуйте другое");
            return T("ui.welcome.name_failed", "Не удалось задать ник — попробуйте ещё раз");
        }

        public override void Unject()
        {
            _continueButton?.onClick.RemoveListener(OnContinueClicked);
            _retryButton?.onClick.RemoveListener(StartGuestLogin);
            _googleButton?.onClick.RemoveListener(OnGoogleClicked);
            _appleButton?.onClick.RemoveListener(OnAppleClicked);
        }

        void SetLoading(bool active)
        {
            if (_loadingOverlay != null) _loadingOverlay.SetActive(active);
            if (_continueButton != null) _continueButton.interactable = !active;
        }

        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }
        void ClearFeedback()          { if (_feedbackText != null) _feedbackText.text = ""; }
    }
}
