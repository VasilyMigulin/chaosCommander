using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChaosCommander.Functions;

/// <summary>
/// Чёрный рынок (Phase 5). Пул в Title Data "blackMarketConfig". Ротация weekly (среда).
/// Купил одну карту → остальные гаснут до ротации. Состояние игрока — UserReadOnlyData "blackMarket".
/// ЗАГОТОВКА.
/// </summary>
public static class BlackMarket
{
    public class Offer
    {
        public string ItemId = ""; public string Rarity = ""; public string PriceCode = "GD";
        public int PriceAmount; public bool Available = true;
    }
    public class BlackMarketResponse
    {
        public int RotationIndex; public string ServerTimeUtc = "";
        public string NextRotationUtc = ""; public bool PurchasedThisRotation;
        public List<Offer> Offers { get; set; } = new();
    }

    [Function("GetBlackMarket")]
    public static async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        // TODO(Phase 5):
        //  - rotationIndex = weekIndex(now, weeklyDayUtc, hourUtc) [та же математика, что и weekly-задачи],
        //  - прочитать UserReadOnlyData "blackMarket"; если rotationIndex вырос → заролить свежий набор
        //    из pools (slots: 4c/3r/2e/1l/1x, детерминированный seed по (playerId, rotationIndex)),
        //    сбросить purchasedThisRotation=false, записать обратно,
        //  - вернуть офферы (available=!purchasedThisRotation && !этот_куплен).
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, new BlackMarketResponse
        {
            ServerTimeUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    [Function("BuyBlackMarketCard")]
    public static async Task<HttpResponseData> Buy(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ItemIdRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new RewardResponse { Success = false, Reason = "not_implemented" };
        // TODO(Phase 5): проверить оффер в текущей ротации и !purchasedThisRotation → списать валюту →
        //  выдать карту → purchasedThisRotation=true (остальные гаснут) → resp.Wallet.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }
}
