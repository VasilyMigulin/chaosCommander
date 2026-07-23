using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Гейт версии приложения. Читает из Title Data минимально требуемую версию и ссылки на сторы,
    /// сравнивает с Application.version. Если версия ниже минимальной — клиент должен показать
    /// блокирующее окно «Обновите приложение» (VersionGateView). Проверять на входе (после логина).
    ///
    /// Title Data (правится удалённо в Game Manager):
    ///   minAppVersion   — напр. "1.4.0"
    ///   storeUrlAndroid — ссылка на Google Play
    ///   storeUrlIOS     — ссылка на App Store
    /// </summary>
    public static class VersionGateService
    {
        public class Result
        {
            public bool   Outdated;
            public string MinVersion;
            public string StoreUrl;
        }

        const string KeyMin     = "minAppVersion";
        const string KeyAndroid = "storeUrlAndroid";
        const string KeyIos     = "storeUrlIOS";

        public static void Check(Action<Result> onDone, Action<string> onError = null)
        {
            PlayFabClientAPI.GetTitleData(
                new GetTitleDataRequest { Keys = new List<string> { KeyMin, KeyAndroid, KeyIos } },
                res =>
                {
                    var data = res.Data;
                    var result = new Result
                    {
                        MinVersion = Get(data, KeyMin),
                        StoreUrl   = PickStoreUrl(data),
                    };
                    result.Outdated = IsOutdated(Application.version, result.MinVersion);
                    onDone?.Invoke(result);
                },
                error =>
                {
                    // Сеть/Title Data недоступны — НЕ блокируем вход (гейт не должен запирать по ошибке связи).
                    Debug.LogWarning($"[VersionGate] GetTitleData failed: {error.ErrorMessage}");
                    onError?.Invoke(error.ErrorMessage);
                });
        }

        static string Get(Dictionary<string, string> data, string key)
            => data != null && data.TryGetValue(key, out var v) ? v : null;

        static string PickStoreUrl(Dictionary<string, string> data)
        {
#if UNITY_IOS
            return Get(data, KeyIos);
#else
            return Get(data, KeyAndroid);
#endif
        }

        static bool IsOutdated(string current, string min)
        {
            if (string.IsNullOrEmpty(min)) return false;
            if (Version.TryParse(current, out var c) && Version.TryParse(min, out var m))
                return c < m;
            return false;   // не смогли распарсить — не блокируем
        }
    }
}
