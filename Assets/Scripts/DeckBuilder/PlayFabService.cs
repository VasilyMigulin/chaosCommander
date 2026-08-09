using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Сервис авторизации и облачного хранилища данных игрока через PlayFab.
    /// Не зависит от ECS.
    ///
    /// Ключи UserData:
    ///   "player_library" — JSON списка OwnedCardData[]
    ///   "player_decks"   — JSON списка SavedDeckData[]
    /// </summary>
    public static class PlayFabService
    {
        const string KEY_LIBRARY = "player_library";
        const string KEY_DECKS   = "player_decks";

        public static bool IsLoggedIn => !string.IsNullOrEmpty(PlayFabSettings.staticPlayer.ClientSessionTicket);

        public static string PlayFabId { get; private set; }

        /// <summary>
        /// Ник аккаунта, с которым вошли (из пейлоада логина). Пусто — ник ещё не задан:
        /// LoginPanel по этому признаку решает, показывать ли экран «Давай знакомиться».
        /// </summary>
        public static string DisplayName { get; private set; }

        // ── Auth ─────────────────────────────────────────────────────────────

        // Единый лог ошибки PlayFab: GenerateErrorReport() даёт код, HTTP-статус и детали (а не только
        // короткий ErrorMessage). По нему сразу видно throttle, неверный TitleId, отключённый метод и т.п.
        static void LogPlayFabError(string where, PlayFabError error)
        {
            Debug.LogError($"[PlayFab] {where} FAILED: code={error.Error} http={error.HttpCode} " +
                           $"msg='{error.ErrorMessage}'\n{error.GenerateErrorReport()}");
        }

        // Параметры «что подтянуть при логине»: UserData + UserReadOnlyData (в ней флаг isDev дев-аккаунта)
        // + профиль ради ника (аккаунт мог быть создан на сайте — тогда ник уже есть, и знакомиться не надо).
        static GetPlayerCombinedInfoRequestParams LoginInfoParams() => new GetPlayerCombinedInfoRequestParams
        {
            GetUserData = true,
            GetUserReadOnlyData = true,
            GetPlayerProfile = true,
            ProfileConstraints = new PlayerProfileViewConstraints { ShowDisplayName = true },
        };

        // Запомнить ник из пейлоада логина (пусто, если ещё не задан).
        static void ApplyDisplayName(GetPlayerCombinedInfoResultPayload info)
        {
            DisplayName = info?.PlayerProfile?.DisplayName ?? "";
        }

        // Прочитать серверный флаг isDev из ответа логина → включить дев-панель для этого аккаунта.
        // UserReadOnlyData клиент писать не может, только сервер/Game Manager — подделать нельзя.
        static void ApplyDevAccount(GetPlayerCombinedInfoResultPayload info)
        {
            bool isDev = info?.UserReadOnlyData != null
                         && info.UserReadOnlyData.TryGetValue("isDev", out var rec)
                         && rec != null && rec.Value == "true";
            Game.Core.Service.DebugFlags.DevAccount = isDev;
            if (isDev) Debug.Log("[PlayFab] Аккаунт разработчика (isDev=true) — дев-панель включена.");
        }

        /// <summary>Войти по email + пароль.</summary>
        public static void LoginWithEmail(string email, string password,
            Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] LoginWithEmail → title={PlayFabSettings.TitleId} email='{email}'");
            PlayFabClientAPI.LoginWithEmailAddress(
                new LoginWithEmailAddressRequest
                {
                    Email    = email,
                    Password = password,
                    InfoRequestParameters = LoginInfoParams()
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    ApplyDevAccount(result.InfoResultPayload);
                    ApplyDisplayName(result.InfoResultPayload);
                    Debug.Log($"[PlayFab] LoginWithEmail OK → PlayFabId={result.PlayFabId} newlyCreated={result.NewlyCreated}");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("LoginWithEmail", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Зарегистрировать новый аккаунт по email + пароль.</summary>
        public static void RegisterWithEmail(string email, string password, string username,
            Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] RegisterWithEmail → title={PlayFabSettings.TitleId} email='{email}' user='{username}'");
            PlayFabClientAPI.RegisterPlayFabUser(
                new RegisterPlayFabUserRequest
                {
                    Email        = email,
                    Password     = password,
                    Username     = username,
                    RequireBothUsernameAndEmail = false,
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    Debug.Log($"[PlayFab] RegisterWithEmail OK → PlayFabId={result.PlayFabId}");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("RegisterWithEmail", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Войти / создать аккаунт по CustomId (устройство).</summary>
        public static void LoginWithCustomId(string customId,
            Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] LoginWithCustomId → title={PlayFabSettings.TitleId} customId='{customId}'");
            PlayFabClientAPI.LoginWithCustomID(
                new LoginWithCustomIDRequest
                {
                    CustomId     = customId,
                    CreateAccount = true,
                    InfoRequestParameters = LoginInfoParams()
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    ApplyDevAccount(result.InfoResultPayload);
                    ApplyDisplayName(result.InfoResultPayload);
                    Debug.Log($"[PlayFab] LoginWithCustomId OK → PlayFabId={result.PlayFabId} newlyCreated={result.NewlyCreated}");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("LoginWithCustomId", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Задать отображаемое имя (ник) текущему игроку. PlayFab: 3–25 символов, может вернуть
        /// NameNotAvailable (занято/запрещено). Ставится после гостевого входа на панели «Давай знакомиться».</summary>
        public static void SetDisplayName(string displayName, Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] SetDisplayName '{displayName}'…");
            PlayFabClientAPI.UpdateUserTitleDisplayName(
                new UpdateUserTitleDisplayNameRequest { DisplayName = displayName },
                result =>
                {
                    DisplayName = result.DisplayName;
                    Debug.Log($"[PlayFab] SetDisplayName OK → '{result.DisplayName}'");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("SetDisplayName", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>
        /// Привязать ID устройства к ТЕКУЩЕМУ аккаунту. Нужен после явного входа (почта/Google/Apple)
        /// на телефоне: со следующего запуска тихий вход по CustomId попадёт уже в этот аккаунт,
        /// а не в гостевой.
        ///
        /// forceLink=true отбирает устройство у гостевого аккаунта, который мог быть создан
        /// автологином. Прогресс того гостя остаётся на нём и становится недоступен — это
        /// осознанный размен: игрок сам выбрал войти в свой аккаунт.
        /// </summary>
        public static void LinkCustomId(string customId, bool forceLink,
            Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] LinkCustomId → customId='{customId}' force={forceLink}");
            PlayFabClientAPI.LinkCustomID(
                new LinkCustomIDRequest { CustomId = customId, ForceLink = forceLink },
                _ => { Debug.Log("[PlayFab] LinkCustomId OK"); onSuccess?.Invoke(); },
                error => { LogPlayFabError("LinkCustomId", error); onError?.Invoke(error.ErrorMessage); });
        }

        // ── Соцпривязка (Google / Apple) ─────────────────────────────────────
        // К РЕЛИЗУ. Базовая личность — гость (CustomId). Эти методы ПРИВЯЗЫВАЮТ соцаккаунт к ТЕКУЩЕМУ
        // залогиненному гостю (тот же PlayFabId, прогресс не теряется). Сам код готов — не хватает только
        // нативной части: получить токен у платформы и передать сюда.
        //   • Google (Android): serverAuthCode из Google Sign-In / Play Games SDK (нужен OAuth client id
        //     в Google Cloud). ВНИМАНИЕ: требует Google Play Services — на RuStore/РФ-Android часто нет.
        //   • Apple (iOS): identityToken из «Sign in with Apple» (нужен Apple Developer + сборка на Mac).

        /// <summary>Привязать Google-аккаунт к текущему (гостевому) игроку.</summary>
        public static void LinkGoogleAccount(string serverAuthCode, Action onSuccess, Action<string> onError)
        {
            Debug.Log("[PlayFab] LinkGoogleAccount…");
            PlayFabClientAPI.LinkGoogleAccount(
                new LinkGoogleAccountRequest { ServerAuthCode = serverAuthCode, ForceLink = false },
                _ => { Debug.Log("[PlayFab] LinkGoogleAccount OK"); onSuccess?.Invoke(); },
                error => { LogPlayFabError("LinkGoogleAccount", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Привязать Apple ID к текущему (гостевому) игроку.</summary>
        public static void LinkApple(string identityToken, Action onSuccess, Action<string> onError)
        {
            Debug.Log("[PlayFab] LinkApple…");
            PlayFabClientAPI.LinkApple(
                new LinkAppleRequest { IdentityToken = identityToken, ForceLink = false },
                _ => { Debug.Log("[PlayFab] LinkApple OK"); onSuccess?.Invoke(); },
                error => { LogPlayFabError("LinkApple", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Вход по Google-аккаунту (если игрок уже привязывал его на другом устройстве).</summary>
        public static void LoginWithGoogleAccount(string serverAuthCode, Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] LoginWithGoogleAccount → title={PlayFabSettings.TitleId}");
            PlayFabClientAPI.LoginWithGoogleAccount(
                new LoginWithGoogleAccountRequest
                {
                    ServerAuthCode = serverAuthCode,
                    CreateAccount = true,
                    InfoRequestParameters = LoginInfoParams()
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    ApplyDevAccount(result.InfoResultPayload);
                    ApplyDisplayName(result.InfoResultPayload);
                    Debug.Log($"[PlayFab] LoginWithGoogleAccount OK → {result.PlayFabId}");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("LoginWithGoogleAccount", error); onError?.Invoke(error.ErrorMessage); });
        }

        /// <summary>Вход по Apple ID (если игрок уже привязывал его).</summary>
        public static void LoginWithApple(string identityToken, Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PlayFab] LoginWithApple → title={PlayFabSettings.TitleId}");
            PlayFabClientAPI.LoginWithApple(
                new LoginWithAppleRequest
                {
                    IdentityToken = identityToken,
                    CreateAccount = true,
                    InfoRequestParameters = LoginInfoParams()
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    ApplyDevAccount(result.InfoResultPayload);
                    ApplyDisplayName(result.InfoResultPayload);
                    Debug.Log($"[PlayFab] LoginWithApple OK → {result.PlayFabId}");
                    onSuccess?.Invoke();
                },
                error => { LogPlayFabError("LoginWithApple", error); onError?.Invoke(error.ErrorMessage); });
        }

        // ── Save ─────────────────────────────────────────────────────────────

        public static void SaveDecks(IEnumerable<SavedDeckData> decks,
            Action onSuccess = null, Action<string> onError = null)
        {
            var list = new DeckList { Decks = new List<SavedDeckData>(decks) };
            SetUserData(KEY_DECKS, JsonUtility.ToJson(list), onSuccess, onError);
        }

        public static void SaveLibrary(IEnumerable<OwnedCardData> owned,
            Action onSuccess = null, Action<string> onError = null)
        {
            var wrapper = new OwnedList { Cards = new List<OwnedCardData>(owned) };
            SetUserData(KEY_LIBRARY, JsonUtility.ToJson(wrapper), onSuccess, onError);
        }

        // ── Load ─────────────────────────────────────────────────────────────

        public static void LoadDecks(Action<List<SavedDeckData>> onSuccess, Action<string> onError = null)
        {
            GetUserData(KEY_DECKS, json =>
            {
                try
                {
                    var list = JsonUtility.FromJson<DeckList>(json);
                    onSuccess?.Invoke(list?.Decks ?? new List<SavedDeckData>());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PlayFabService] Failed to parse decks: {e.Message}");
                    onSuccess?.Invoke(new List<SavedDeckData>());
                }
            }, onError);
        }

        public static void LoadLibrary(Action<List<OwnedCardData>> onSuccess, Action<string> onError = null)
        {
            GetUserData(KEY_LIBRARY, json =>
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<OwnedList>(json);
                    onSuccess?.Invoke(wrapper?.Cards ?? new List<OwnedCardData>());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PlayFabService] Failed to parse library: {e.Message}");
                    onSuccess?.Invoke(new List<OwnedCardData>());
                }
            }, onError);
        }

        // ── Internal ─────────────────────────────────────────────────────────

        static void SetUserData(string key, string json,
            Action onSuccess, Action<string> onError)
        {
            // Без логина (в т.ч. SkipLogin) PlayFab кидает СИНХРОННОЕ исключение «Must be logged in» —
            // мимо onError, с падением вызывающей панели. Грейсфул: сообщаем ошибку, кеш в памяти цел.
            if (!PlayFabClientAPI.IsClientLoggedIn())
            {
                onError?.Invoke("not_logged_in");
                return;
            }

            PlayFabClientAPI.UpdateUserData(
                new UpdateUserDataRequest
                {
                    Data = new Dictionary<string, string> { { key, json } },
                    Permission = UserDataPermission.Private,
                },
                _ => onSuccess?.Invoke(),
                error => onError?.Invoke(error.ErrorMessage));
        }

        static void GetUserData(string key, Action<string> onSuccess, Action<string> onError)
        {
            // См. SetUserData: без логина PlayFab кидает синхронное исключение, а не зовёт onError.
            if (!PlayFabClientAPI.IsClientLoggedIn())
            {
                onError?.Invoke("not_logged_in");
                return;
            }

            PlayFabClientAPI.GetUserData(
                new GetUserDataRequest { Keys = new List<string> { key } },
                result =>
                {
                    if (result.Data != null && result.Data.TryGetValue(key, out var record))
                        onSuccess?.Invoke(record.Value);
                    else
                        onSuccess?.Invoke("{}");
                },
                error => onError?.Invoke(error.ErrorMessage));
        }

        // ── Serialization helpers ─────────────────────────────────────────────

        [Serializable] class DeckList  { public List<SavedDeckData>  Decks; }
        [Serializable] class OwnedList { public List<OwnedCardData>  Cards; }
    }
}
