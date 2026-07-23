using System;
using System.Collections.Generic;
using PlayFab.ClientModels;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// Открытие бустеров. Ролл карт — на сервере (Azure Function OpenBooster) по дроп-таблице
    /// из Title Data: проверяет владение → списывает бустер → роллит карты по весам редкости →
    /// выдаёт в инвентарь → возвращает список для reveal-анимации. RNG на сервере = нечитабельно.
    /// </summary>
    public static class BoosterService
    {
        [Serializable] public class OpenRequest { public string BoosterItemId; public int Count = 1; }

        /// <summary>Ответ открытия: результат + выданные карты (Reward.Cards) + сколько бустеров реально открыто.</summary>
        [Serializable] public class OpenResponse : RewardResponse { public int Opened; }

        /// <summary>Потолок мульти-открытия (должен совпадать с сервером MAX_OPEN_AT_ONCE).</summary>
        public const int MaxOpenAtOnce = 5;

        /// <summary>Список бустеров в инвентаре игрока (сгруппировано item id → количество).</summary>
        public static void GetOwned(Action<Dictionary<string, int>> onSuccess, Action<string> onError = null)
        {
            EconomyService.GetInventory(inv =>
            {
                var map = new Dictionary<string, int>();
                foreach (ItemInstance b in inv.Boosters)
                {
                    if (b?.ItemId == null) continue;
                    map.TryGetValue(b.ItemId, out var cur);
                    map[b.ItemId] = cur + 1;   // v1: каждый инстанс = 1 бустер
                }
                onSuccess?.Invoke(map);
            }, onError);
        }

        /// <summary>Открыть один бустер. Ответ.Reward.Cards = выпавшие карты.</summary>
        public static void Open(string boosterItemId, Action<OpenResponse> onSuccess, Action<string> onError = null)
            => Open(boosterItemId, 1, onSuccess, onError);

        /// <summary>Открыть до count бустеров за раз (сервер откроет сколько есть, не больше MaxOpenAtOnce).
        /// Reward.Cards — все выпавшие карты (агрегированы), Opened — сколько реально открыто.</summary>
        public static void Open(string boosterItemId, int count, Action<OpenResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<OpenRequest, OpenResponse>(
                BackendConfig.Fn.OpenBooster, new OpenRequest { BoosterItemId = boosterItemId, Count = count },
                r =>
                {
                    if (r != null && r.Success)
                    {
                        PlayerWallet.ApplyIfPresent(r.Wallet);
                        PlayerLibrary.AddGranted(r.Reward?.Cards, BackendSession.Config);   // выпавшие карты → в библиотеку
                    }
                    onSuccess?.Invoke(r);
                }, onError);
    }
}
