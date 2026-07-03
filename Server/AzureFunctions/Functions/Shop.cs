using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChaosCommander.Functions;

/// <summary>
/// Магазин (Phase 3). Витрина читается из Economy Store, покупка списывает валюту + выдаёт предмет.
/// ЗАГОТОВКА: реализовать по паттерну OpenBooster (server-authoritative).
/// </summary>
public static class Shop
{
    public class ShopEntry
    {
        public string ItemId = ""; public string DisplayName = ""; public string Category = "";
        public string PriceCode = "GD"; public int PriceAmount; public bool AlreadyOwned;
    }
    public class ShopResponse { public List<ShopEntry> Entries { get; set; } = new(); }

    [Function("GetShop")]
    public static async Task<HttpResponseData> GetShop(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        // TODO(Phase 3): PlayFabEconomyInstanceAPI.SearchItems по StoreId → собрать ShopEntry с ценами
        // из Store (PriceOptions). Для durable-аватаров проставить AlreadyOwned по инвентарю игрока.
        var _ = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        return await FunctionHttp.JsonAsync(req, new ShopResponse());
    }

    [Function("BuyStoreItem")]
    public static async Task<HttpResponseData> Buy(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ItemIdRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new RewardResponse { Success = false, Reason = "not_implemented" };

        // TODO(Phase 3):
        //  1) прочитать цену предмета из Store/Catalog (НЕ из клиента),
        //  2) TrySpendCurrencyAsync(entity, code, amount) — при неудаче Reason="insufficient_funds",
        //  3) GrantItemAsync(entity, itemId, 1) (бустер/аватар),
        //  4) resp.Reward.* + resp.Wallet = ReadWalletAsync(entity), Success=true.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }
}
