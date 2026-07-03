using System;
using System.Collections.Generic;

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

        [Serializable] public class BuyRequest { public string ItemId; }

        public static void GetShop(Action<ShopResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetShop, onSuccess, onError);

        /// <summary>Купить предмет магазина. Ответ содержит выданное (бустер/аватар) + свежий кошелёк.</summary>
        public static void Buy(string itemId, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<BuyRequest, RewardResponse>(
                BackendConfig.Fn.BuyStoreItem, new BuyRequest { ItemId = itemId },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);
    }
}
