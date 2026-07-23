using System;
using System.Collections.Generic;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// Внутриигровой магазин: бустеры расширений + аватары. Витрину и цены отдаёт сервер
    /// (читает Store/Catalog Economy v2), покупка проводится серверной функцией BuyStoreItem
    /// (списывает валюту + выдаёт предмет) — клиент не оперирует ценами/currency-id.
    /// </summary>
    public static class ShopService
    {
        [Serializable]
        public class ShopEntry
        {
            public string ItemId;         // "booster_standard" / "avatar_xxx"
            public string DisplayName;
            public string Category;        // "booster" / "avatar"
            public string PriceCode;       // "GD" / "GM"
            public int    PriceAmount;
            public bool   AlreadyOwned;    // для durable (аватары)
        }

        [Serializable]
        public class ShopResponse
        {
            public List<ShopEntry> Entries = new List<ShopEntry>();
        }

        [Serializable] public class BuyRequest { public string ItemId; public int Count = 1; }

        public static void GetShop(Action<ShopResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetShop, onSuccess, onError);

        /// <summary>Купить предмет магазина. Ответ содержит выданное (бустер/аватар) + свежий кошелёк.</summary>
        /// <summary>Потолок покупки пачкой (должен совпадать с сервером MAX_BUY_AT_ONCE). Фактический
        /// предел обычно упирается в деньги игрока, а не в это число.</summary>
        public const int MaxBuyAtOnce = 99;

        public static void Buy(string itemId, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => Buy(itemId, 1, onSuccess, onError);

        /// <summary>Купить count единиц за раз. Уникальное (аватары) сервер всё равно ограничит одной.</summary>
        public static void Buy(string itemId, int count, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<BuyRequest, RewardResponse>(
                BackendConfig.Fn.BuyStoreItem, new BuyRequest { ItemId = itemId, Count = count },
                resp =>
                {
                    // Кошелёк применяем всегда: сервер шлёт его и на отказах (напр. не хватило валюты).
                    PlayerWallet.ApplyIfPresent(resp?.Wallet);
                    // Витрина умеет продавать и КАРТЫ (category="card") — их сразу в библиотеку,
                    // иначе коллекция обновится только при перезаходе. Бустеры/аватары не карты — no-op.
                    if (resp != null && resp.Success)
                        PlayerLibrary.AddGranted(resp.Reward?.Cards, BackendSession.Config);
                    onSuccess?.Invoke(resp);
                }, onError);
    }
}
