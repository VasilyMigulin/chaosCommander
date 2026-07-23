using System;
using System.Collections.Generic;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// Чёрный рынок: ротируемый курируемый пул карт на продажу. Пул задаётся в Title Data
    /// (по expansionId+cardId, разбит по редкостям). Сервер роллит набор 4 common / 3 rare /
    /// 2 epic / 1 legendary / 1 exotic на ротацию. Купил ОДНУ карту → остальные недоступны
    /// до следующей ротации (по умолчанию — среда). Всё считает сервер.
    /// </summary>
    public static class BlackMarketService
    {
        [Serializable]
        public class Offer
        {
            public string ItemId;       // "{expansionId}_{cardId}"
            public string Rarity;       // "Common".."Exotic"
            public string PriceCode;    // "GD"/"GM"
            public int    PriceAmount;
            public bool   Available;    // false после покупки в этой ротации / если куплена

            public bool TryResolve(out string expansionId, out int cardId)
                => CardItemId.TryParse(ItemId, out expansionId, out cardId);
        }

        [Serializable]
        public class BlackMarketResponse
        {
            public int    RotationIndex;
            public string ServerTimeUtc;
            public string NextRotationUtc;
            public bool   PurchasedThisRotation;
            public List<Offer> Offers = new List<Offer>();
        }

        [Serializable] public class BuyRequest { public string ItemId; }

        public static void Get(Action<BlackMarketResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetBlackMarket, onSuccess, onError);

        /// <summary>Купить карту с чёрного рынка. После успеха остальные офферы гаснут до ротации.</summary>
        public static void Buy(string itemId, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<BuyRequest, RewardResponse>(
                BackendConfig.Fn.BuyBlackMarket, new BuyRequest { ItemId = itemId },
                resp =>
                {
                    // Кошелёк применяем всегда: сервер шлёт его и на отказах (напр. не хватило валюты).
                    PlayerWallet.ApplyIfPresent(resp?.Wallet);
                    // Купленную карту — в библиотеку, иначе коллекция обновится только при перезаходе.
                    if (resp != null && resp.Success)
                        PlayerLibrary.AddGranted(resp.Reward?.Cards, BackendSession.Config);
                    onSuccess?.Invoke(resp);
                }, onError);
    }
}
