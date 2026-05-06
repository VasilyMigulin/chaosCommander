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

        // ── Auth ─────────────────────────────────────────────────────────────

        /// <summary>Войти по email + пароль.</summary>
        public static void LoginWithEmail(string email, string password,
            Action onSuccess, Action<string> onError)
        {
            PlayFabClientAPI.LoginWithEmailAddress(
                new LoginWithEmailAddressRequest
                {
                    Email    = email,
                    Password = password,
                    InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                    {
                        GetUserData = true,
                    }
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    onSuccess?.Invoke();
                },
                error => onError?.Invoke(error.ErrorMessage));
        }

        /// <summary>Зарегистрировать новый аккаунт по email + пароль.</summary>
        public static void RegisterWithEmail(string email, string password, string username,
            Action onSuccess, Action<string> onError)
        {
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
                    onSuccess?.Invoke();
                },
                error => onError?.Invoke(error.ErrorMessage));
        }

        /// <summary>Войти / создать аккаунт по CustomId (устройство).</summary>
        public static void LoginWithCustomId(string customId,
            Action onSuccess, Action<string> onError)
        {
            PlayFabClientAPI.LoginWithCustomID(
                new LoginWithCustomIDRequest
                {
                    CustomId     = customId,
                    CreateAccount = true,
                    InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                    {
                        GetUserData = true,
                    }
                },
                result =>
                {
                    PlayFabId = result.PlayFabId;
                    onSuccess?.Invoke();
                },
                error => onError?.Invoke(error.ErrorMessage));
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
