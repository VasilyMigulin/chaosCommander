using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChaosCommander.Functions;

/// <summary>
/// Аукцион (Phase 6, сложнейший). Модель: escrow в Economy (карта списывается у продавца при
/// листинге) + индекс листингов во внешней БД (Azure Table, AUCTION_TABLE_*). Покупка — атомарная
/// транзакция с ETag-конкуренцией по записи листинга. ЗАГОТОВКИ (реализовать последними).
/// </summary>
public static class Auction
{
    public class Listing
    {
        public string ListingId = ""; public string ItemId = ""; public string SellerId = "";
        public string SellerName = ""; public string PriceCode = "GD"; public int PriceAmount;
        public string CreatedAtUtc = "";
    }
    public class ListingsResponse { public List<Listing> Listings { get; set; } = new(); public string? ContinuationToken; }
    public class ListResult : BackendResult { public string? ListingId; }

    [Function("GetAuctionListings")]
    public static async Task<HttpResponseData> GetListings(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        // TODO(Phase 6): запрос к Azure Table с фильтром (ItemId/Rarity/MaxPrice) + пагинация.
        _ = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        return await FunctionHttp.JsonAsync(req, new ListingsResponse());
    }

    [Function("GetMyAuctionListings")]
    public static async Task<HttpResponseData> GetMine(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        // TODO(Phase 6): выбрать листинги, где SellerId == ctx.MasterPlayerAccountId.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, new ListingsResponse());
    }

    [Function("ListCardForSale")]
    public static async Task<HttpResponseData> ListCard(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ListCardRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new ListResult { Success = false, Reason = "not_implemented" };
        // TODO(Phase 6): валидировать цену (min/max из Title Data) → SubtractInventoryItems карту у
        //  продавца (escrow) → создать запись листинга в Azure Table → resp.ListingId.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }

    [Function("CancelAuctionListing")]
    public static async Task<HttpResponseData> Cancel(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ListingIdRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new BackendResult { Success = false, Reason = "not_implemented" };
        // TODO(Phase 6): проверить владельца листинга → вернуть карту в инвентарь → удалить запись.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }

    [Function("BuyAuctionListing")]
    public static async Task<HttpResponseData> Buy(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ListingIdRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new RewardResponse { Success = false, Reason = "not_implemented" };
        // TODO(Phase 6): транзакция с ETag — пометить листинг sold → списать валюту покупателя →
        //  начислить продавцу (минус комиссия = gold sink) → выдать карту покупателю → resp.Wallet.
        //  При гонке (ETag конфликт) — откат/повтор, Reason="already_sold".
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }
}
