using System;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Типизированная обёртка над PlayFab Classic CloudScript (ExecuteCloudScript).
    /// Все мутации экономики/наград/сделок идут через сервер (JS-ревизия в Game Manager);
    /// клиент только просит. Имена функций = имена handlers.* в CloudScript-ревизии.
    ///
    /// FunctionResult у PlayFab приходит как object (вложенный JSON) — десериализуем через
    /// штатный PlayFab-сериализатор (публичный ISerializerPlugin), чтобы не тащить Newtonsoft
    /// и одинаково разбирать даты/enum-ы.
    /// </summary>
    public static class FunctionService
    {
        static readonly ISerializerPlugin Json =
            PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer);

        /// <summary>Вызвать функцию с типизированными параметром и ответом.</summary>
        public static void Call<TReq, TResp>(string functionName, TReq parameter,
            Action<TResp> onSuccess, Action<string> onError = null)
            where TResp : class
        {
            // Без логина (в т.ч. SkipLogin / до авторизации) серверные вызовы невозможны —
            // ExecuteCloudScript кинул бы исключение «Must be logged in». Грейсфул: onError, без краша.
            if (!PlayFabClientAPI.IsClientLoggedIn())
            {
                onError?.Invoke("not_logged_in");
                return;
            }

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = functionName,
                    FunctionParameter = parameter,
                    GeneratePlayStreamEvent = false,
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        var msg = $"{result.Error.Error}: {result.Error.Message}";
                        Debug.LogWarning($"[FunctionService] {functionName} function error — {msg}");
                        onError?.Invoke(msg);
                        return;
                    }

                    TResp typed = null;
                    try
                    {
                        typed = Parse<TResp>(result.FunctionResult);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[FunctionService] {functionName} parse failed: {e.Message}");
                        onError?.Invoke($"parse error: {e.Message}");
                        return;
                    }
                    onSuccess?.Invoke(typed);
                },
                error =>
                {
                    Debug.LogWarning($"[FunctionService] {functionName} call failed: {error.ErrorMessage}");
                    onError?.Invoke(error.ErrorMessage);
                });
        }

        /// <summary>Вызов без параметра.</summary>
        public static void Call<TResp>(string functionName,
            Action<TResp> onSuccess, Action<string> onError = null)
            where TResp : class
            => Call<object, TResp>(functionName, null, onSuccess, onError);

        /// <summary>Вызов без ожидаемого ответа (fire-and-check).</summary>
        public static void Call<TReq>(string functionName, TReq parameter,
            Action onSuccess, Action<string> onError = null)
            => Call<TReq, EmptyResponse>(functionName, parameter, _ => onSuccess?.Invoke(), onError);

        static T Parse<T>(object functionResult) where T : class
        {
            if (functionResult == null) return null;
            // object → JSON → T (PlayFab-стратегия для дат/enum-ов).
            string json = Json.SerializeObject(functionResult);
            return Json.DeserializeObject<T>(json);
        }

        [Serializable] public class EmptyResponse { }
    }
}
