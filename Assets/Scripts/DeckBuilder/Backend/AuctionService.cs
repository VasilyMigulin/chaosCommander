using System;
using System.Collections.Generic;

namespace Game.Core.Backend
{
    /// <summary>
    /// Аукцион: игроки выставляют на продажу СВОИ карты. Реализация — Economy escrow (карта
    /// списывается у продавца при листинге) + серверная функция + внешний индекс листингов
    /// (Azure Table). Покупка — атомарная транзакция на сервере (debit покупателя → credit
    /// продавца минус комиссия → выдача карты). Клиент только листает/просит.
    /// </summary>
    public static class AuctionService
    {
        [Serializable]
        public class Listing
        {
            public string ListingId;
            public string ItemId;        // "{expansionId}_{cardId}"
            public string SellerId;
            public string SellerName;
            public string PriceCode;     // "GD"/"GM"
            public int    PriceAmount;
            public string CreatedAtUtc;

            public bool TryResolve(out string expansionId, out int cardId)
                => CardItemId.TryParse(ItemId, out expansionId, out cardId);
        }

        [Serializable]
        public class ListingsResponse
        {
            public List<Listing> Listings = new List<Listing>();
            public string ContinuationToken;   // пагинация индекса
        }

        // Запросы
        [Serializable]
        public class GetListingsRequest
        {
            public string ItemId;              // фильтр по конкретной карте (опц.)
            public string Rarity;              // фильтр по редкости (опц.)
            public int    MaxPrice;            // 0 = без лимита
            public string ContinuationToken;
        }

        [Serializable] public class ListCardRequest   { public string ItemId; public int PriceAmount; public string PriceCode = BackendConfig.GoldCode; }
        [Serializable] public class ListingIdRequest  { public string ListingId; }

        [Serializable] public class ListResult : BackendResult { public string ListingId; }

        // ── Чтение ───────────────────────────────────────────────────────────

        public static void GetListings(GetListingsRequest filter,
            Action<ListingsResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<GetListingsRequest, ListingsResponse>(
                BackendConfig.Fn.GetListings, filter ?? new GetListingsRequest(), onSuccess, onError);

        public static void GetMyListings(Action<ListingsResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetMyListings, onSuccess, onError);

        // ── Мутации ──────────────────────────────────────────────────────────

        /// <summary>Выставить свою карту. Карта сразу уходит из инвентаря в escrow.</summary>
        public static void ListCard(string itemId, int priceAmount,
            Action<ListResult> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ListCardRequest, ListResult>(
                BackendConfig.Fn.ListCard,
                new ListCardRequest { ItemId = itemId, PriceAmount = priceAmount },
                onSuccess, onError);

        /// <summary>Снять свой листинг — карта возвращается в инвентарь.</summary>
        public static void CancelListing(string listingId,
            Action<BackendResult> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ListingIdRequest, BackendResult>(
                BackendConfig.Fn.CancelListing, new ListingIdRequest { ListingId = listingId },
                onSuccess, onError);

        /// <summary>Купить листинг. Ответ.Reward.Cards = купленная карта; списывается кошелёк.</summary>
        public static void BuyListing(string listingId,
            Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ListingIdRequest, RewardResponse>(
                BackendConfig.Fn.BuyListing, new ListingIdRequest { ListingId = listingId },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);
    }
}
