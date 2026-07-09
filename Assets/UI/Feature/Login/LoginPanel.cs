using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.DeckBuilder;
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
    /// Панель входа / регистрации.
    ///
    /// Структура сцены (Inspector):
    ///   LoginPanel
    ///     ├─ EmailTab          (Button) ← _emailTabButton
    ///     ├─ GuestTab          (Button) ← _guestTabButton
    ///     │
    ///     ├─ EmailView         (GameObject) ← _emailView
    ///     │    ├─ EmailInput   (TMP_InputField) ← _loginEmailInput
    ///     │    ├─ PasswordInput(TMP_InputField) ← _loginPasswordInput
    ///     │    ├─ RememberMe   (Toggle)         ← _rememberMeToggle
    ///     │    ├─ LoginButton  (Button) ← _loginButton
    ///     │    └─ SignUpButton  (Button) ← _signUpButton   ← открывает RegisterView
    ///     │
    ///     ├─ RegisterView      (GameObject) ← _registerView
    ///     │    ├─ RegEmailInput   (TMP_InputField) ← _regEmailInput
    ///     │    ├─ RegPasswordInput(TMP_InputField) ← _regPasswordInput
    ///     │    ├─ UsernameInput   (TMP_InputField) ← _usernameInput
    ///     │    ├─ RegisterButton  (Button) ← _registerButton
    ///     │    └─ BackToLoginButton(Button)← _backToLoginButton
    ///     │
    ///     ├─ GuestView         (GameObject) ← _guestView
    ///     │    ├─ CustomIdText (TMP_Text)  ← _customIdText
    ///     │    ├─ RememberMe   (Toggle)    ← _rememberMeGuestToggle
    ///     │    └─ GuestButton  (Button)    ← _guestLoginButton
    ///     │
    ///     ├─ LoadingOverlay    (GameObject) ← _loadingOverlay
    ///     └─ FeedbackText      (TMP_Text)   ← _feedbackText
    /// </summary>
    public class LoginPanel : SourcePanel
    {
        [Header("Tabs")]
        [SerializeField] Button     _emailTabButton;
        [SerializeField] Button     _guestTabButton;
        [SerializeField] GameObject _emailView;
        [SerializeField] GameObject _registerView;
        [SerializeField] GameObject _guestView;

        [Header("Email Login")]
        [SerializeField] TMP_InputField _loginEmailInput;
        [SerializeField] TMP_InputField _loginPasswordInput;
        [SerializeField] Toggle         _rememberMeToggle;
        [SerializeField] Button         _loginButton;
        [SerializeField] Button         _signUpButton;

        [Header("Register")]
        [SerializeField] TMP_InputField _regEmailInput;
        [SerializeField] TMP_InputField _regPasswordInput;
        [SerializeField] TMP_InputField _usernameInput;
        [SerializeField] Button         _registerButton;
        [SerializeField] Button         _backToLoginButton;

        [Header("Guest Auth")]
        [SerializeField] TMP_Text _customIdText;
        [SerializeField] Toggle   _rememberMeGuestToggle;
        [SerializeField] Button   _guestLoginButton;

        [Header("Shared")]
        [SerializeField] GameObject _loadingOverlay;
        [SerializeField] TMP_Text   _feedbackText;

        /// <summary>Вызывается после успешного входа любым методом.</summary>
        public event Action OnLoginSuccess;

        const string PrefEmail      = "saved_email";
        const string PrefPassword   = "saved_password";
        const string PrefRemember   = "remember_me";
        const string PrefAuthMethod = "auth_method";  // "email" | "guest"
        const float  ErrorDisplayDelay = 0.6f;        // секунд перед скрытием оверлея при ошибке

        bool _isLoggingIn;

        [UIInject] IInitStateContext _initContext;

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
        }

        public override void OnInject()
        {
            base.OnInject();

            // Снимаем перед добавлением — защита от повторного вызова OnInject
            Unject();

            _emailTabButton?.onClick.AddListener(ShowEmailView);
            _guestTabButton?.onClick.AddListener(ShowGuestView);
            _signUpButton?.onClick.AddListener(ShowRegisterView);
            _backToLoginButton?.onClick.AddListener(ShowEmailView);
            _loginButton?.onClick.AddListener(OnLoginClicked);
            _registerButton?.onClick.AddListener(OnRegisterClicked);
            _guestLoginButton?.onClick.AddListener(OnGuestLoginClicked);

            if (_customIdText != null)
                _customIdText.text = $"Device ID: {SystemInfo.deviceUniqueIdentifier[..8]}...";

            Debug.Log($"[LoginPanel] OnInject — emailTab:{_emailTabButton != null} guestTab:{_guestTabButton != null} emailView:{_emailView != null} guestView:{_guestView != null} registerView:{_registerView != null}");

            SetLoading(false);
            ShowEmailView();

            // ЦИКЛ ПЕРВОГО ЗАХОДА: язык не выбран → АВТОЛОГИН НЕ запускаем (панель языка открывает
            // LoginCanvas.InvokeCanvas — единая точка роутинга; иначе автологин уносил в меню до выбора).
            if (!Game.Core.Service.FirstRunFlow.LanguageChosen)
                return;

            // Автологин если сохранены данные
            if (!_isLoggingIn && PlayerPrefs.GetInt(PrefRemember, 0) == 1)
            {
                string method = PlayerPrefs.GetString(PrefAuthMethod, "");

                if (method == "guest")
                {
                    if (_rememberMeGuestToggle != null) _rememberMeGuestToggle.isOn = true;
                    SetLoading(true);
                    _isLoggingIn = true;
                    PlayFabService.LoginWithCustomId(SystemInfo.deviceUniqueIdentifier,
                        onSuccess: HandleSuccess,
                        onError:   err => { _isLoggingIn = false; SetLoading(false); });
                }
                else if (method == "email")
                {
                    string savedEmail    = PlayerPrefs.GetString(PrefEmail,    "");
                    string savedPassword = PlayerPrefs.GetString(PrefPassword, "");
                    if (!string.IsNullOrEmpty(savedEmail) && !string.IsNullOrEmpty(savedPassword))
                    {
                        if (_loginEmailInput    != null) _loginEmailInput.text    = savedEmail;
                        if (_loginPasswordInput != null) _loginPasswordInput.text = savedPassword;
                        if (_rememberMeToggle   != null) _rememberMeToggle.isOn   = true;
                        SetLoading(true);
                        _isLoggingIn = true;
                        PlayFabService.LoginWithEmail(savedEmail, savedPassword,
                            onSuccess: HandleSuccess,
                            onError:   err => { _isLoggingIn = false; SetLoading(false); });
                    }
                }
            }
        }

        public override void Unject()
        {
            _emailTabButton?.onClick.RemoveListener(ShowEmailView);
            _guestTabButton?.onClick.RemoveListener(ShowGuestView);
            _signUpButton?.onClick.RemoveListener(ShowRegisterView);
            _backToLoginButton?.onClick.RemoveListener(ShowEmailView);
            _loginButton?.onClick.RemoveListener(OnLoginClicked);
            _registerButton?.onClick.RemoveListener(OnRegisterClicked);
            _guestLoginButton?.onClick.RemoveListener(OnGuestLoginClicked);
        }

        // ── Tabs ─────────────────────────────────────────────────────────────

        void ShowEmailView()
        {
            _emailView?.SetActive(true);
            _registerView?.SetActive(false);
            _guestView?.SetActive(false);
            ClearFeedback();
        }

        void ShowRegisterView()
        {
            _emailView?.SetActive(false);
            _registerView?.SetActive(true);
            _guestView?.SetActive(false);
            ClearFeedback();
        }

        void ShowGuestView()
        {
            _emailView?.SetActive(false);
            _registerView?.SetActive(false);
            _guestView?.SetActive(true);
            ClearFeedback();
        }

        // ── Login handlers ────────────────────────────────────────────────────

        void OnLoginClicked()
        {
            if (_isLoggingIn) return;
            string email    = _loginEmailInput    != null ? _loginEmailInput.text.Trim()    : "";
            string password = _loginPasswordInput != null ? _loginPasswordInput.text.Trim() : "";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowFeedback(Game.Core.Shared.CardTextLocalization.GetText("ui.login.enter_credentials", "Введите email и пароль"));
                return;
            }

            SetLoading(true);
            _isLoggingIn = true;
            SaveCredentialsIfNeeded(email, password);
            PlayFabService.LoginWithEmail(email, password,
                onSuccess: HandleSuccess,
                onError:   HandleError);
        }

        void OnRegisterClicked()
        {
            if (_isLoggingIn) return;
            string email    = _regEmailInput    != null ? _regEmailInput.text.Trim()    : "";
            string password = _regPasswordInput != null ? _regPasswordInput.text.Trim() : "";
            string username = _usernameInput     != null ? _usernameInput.text.Trim()     : "Player";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowFeedback(Game.Core.Shared.CardTextLocalization.GetText("ui.login.enter_credentials", "Введите email и пароль"));
                return;
            }
            if (password.Length < 6)
            {
                ShowFeedback(Game.Core.Shared.CardTextLocalization.GetText("ui.login.password_too_short", "Пароль должен быть не менее 6 символов"));
                return;
            }

            SetLoading(true);
            _isLoggingIn = true;
            PlayFabService.RegisterWithEmail(email, password, username,
                onSuccess: () =>
                {
                    SaveCredentialsIfNeeded(email, password);
                    PlayFabService.LoginWithEmail(email, password,
                        onSuccess: HandleSuccess,
                        onError:   HandleError);
                },
                onError: HandleError);
        }

        void OnGuestLoginClicked()
        {
            if (_isLoggingIn) return;
            bool remember = _rememberMeGuestToggle != null && _rememberMeGuestToggle.isOn;
            if (remember)
            {
                PlayerPrefs.SetInt(PrefRemember,      1);
                PlayerPrefs.SetString(PrefAuthMethod, "guest");
            }
            else
            {
                PlayerPrefs.DeleteKey(PrefRemember);
                PlayerPrefs.DeleteKey(PrefAuthMethod);
            }
            PlayerPrefs.Save();

            SetLoading(true);
            _isLoggingIn = true;
            PlayFabService.LoginWithCustomId(SystemInfo.deviceUniqueIdentifier,
                onSuccess: HandleSuccess,
                onError:   HandleError);
        }

        // ── Shared outcome ────────────────────────────────────────────────────

        void HandleSuccess()
        {
            SetLoading(false);
            OnLoginSuccess?.Invoke();
            if (_initContext != null)
                _initContext.OnLoginSuccess();
            else
                SceneManager.LoadScene(1);
        }

        void HandleError(string error)
        {
            _isLoggingIn = false;
            StartCoroutine(ShowErrorDelayed(error));
        }

        IEnumerator ShowErrorDelayed(string error)
        {
            yield return new WaitForSeconds(ErrorDisplayDelay);
            SetLoading(false);
            ShowFeedback(error);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void SaveCredentialsIfNeeded(string email, string password)
        {
            bool remember = _rememberMeToggle != null && _rememberMeToggle.isOn;
            if (remember)
            {
                PlayerPrefs.SetInt(PrefRemember,      1);
                PlayerPrefs.SetString(PrefAuthMethod, "email");
                PlayerPrefs.SetString(PrefEmail,      email);
                PlayerPrefs.SetString(PrefPassword,   password);
            }
            else
            {
                PlayerPrefs.DeleteKey(PrefRemember);
                PlayerPrefs.DeleteKey(PrefAuthMethod);
                PlayerPrefs.DeleteKey(PrefEmail);
                PlayerPrefs.DeleteKey(PrefPassword);
            }
            PlayerPrefs.Save();
        }

        void SetLoading(bool active)
        {
            _loadingOverlay?.SetActive(active);
            if (_loginButton    != null) _loginButton.interactable    = !active;
            if (_registerButton != null) _registerButton.interactable = !active;
            if (_guestLoginButton != null) _guestLoginButton.interactable = !active;
        }

        void ShowFeedback(string msg)
        {
            if (_feedbackText != null) _feedbackText.text = msg;
        }

        void ClearFeedback()
        {
            if (_feedbackText != null) _feedbackText.text = "";
        }
    }
}
