using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;

namespace ChaosCommander.Functions;

/// <summary>
/// Открытие бустера — server-authoritative ролл по дроп-таблице (Title Data "boosterConfig")
/// из пула карт (Title Data "cardPool": expansion → rarity → [itemId]). Порядок:
///   1) списать 1 бустер (если нет — отказ, ничего не выдаём),
///   2) для каждого слота выбрать редкость по весам → случайную карту этой редкости+экспеншена,
///   3) выдать карты в инвентарь и вернуть список для reveal.
/// RNG на сервере = нечитаемо.
/// </summary>
public static class OpenBooster
{
    static readonly Random Rng = new();

    class BoosterCfg { public string expansion = ""; public int cardCount; public List<Slot> slots = new(); }
    class Slot { public Dictionary<string, double> weights = new(); }

    [Function("OpenBooster")]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<BoosterOpenRequest>(await FunctionHttp.ReadBodyAsync(req));
        var entity = PlayFabServer.EntityOf(ctx);
        var resp = new RewardResponse { Success = false };

        string boosterId = ctx.Argument?.BoosterItemId ?? "";
        if (string.IsNullOrEmpty(boosterId))
        {
            resp.Reason = "bad_request";
            return await FunctionHttp.JsonAsync(req, resp);
        }

        // Конфиг
        var configJson = await PlayFabServer.GetTitleDataAsync("boosterConfig");
        var poolJson = await PlayFabServer.GetTitleDataAsync("cardPool");
        if (configJson == null || poolJson == null)
        {
            resp.Reason = "config_missing";
            return await FunctionHttp.JsonAsync(req, resp);
        }
        var allCfg = JsonConvert.DeserializeObject<Dictionary<string, BoosterCfg>>(configJson);
        var pool = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<string>>>>(poolJson);
        if (allCfg == null || !allCfg.TryGetValue(boosterId, out var cfg) || pool == null)
        {
            resp.Reason = "unknown_booster";
            return await FunctionHttp.JsonAsync(req, resp);
        }

        // 1) Списать бустер ПЕРЕД роллом (анти-дюп)
        var economy = new PlayFabEconomyInstanceAPI(PlayFabServer.TitleSettings());
        var spend = await economy.SubtractInventoryItemsAsync(new SubtractInventoryItemsRequest
        {
            Entity = entity, Amount = 1, Item = new InventoryItemReference { Id = boosterId },
        });
        if (spend.Error != null)
        {
            resp.Reason = "not_owned";
            return await FunctionHttp.JsonAsync(req, resp);
        }

        // 2) Ролл слотов
        pool.TryGetValue(cfg.expansion, out var byRarity);
        byRarity ??= new();

        var granted = new Dictionary<string, int>();
        foreach (var slot in cfg.slots)
        {
            string rarity = RollRarity(slot.weights);
            if (!byRarity.TryGetValue(rarity, out var ids) || ids.Count == 0) continue;
            string itemId = ids[Rng.Next(ids.Count)];
            granted.TryGetValue(itemId, out var cur);
            granted[itemId] = cur + 1;
        }

        // 3) Выдать
        foreach (var kv in granted)
        {
            await PlayFabServer.GrantItemAsync(entity, kv.Key, kv.Value);
            resp.Reward.Cards.Add(new GrantedCard { ItemId = kv.Key, Amount = kv.Value });
        }

        resp.Success = true;
        return await FunctionHttp.JsonAsync(req, resp);
    }

    static string RollRarity(Dictionary<string, double> weights)
    {
        double total = 0; foreach (var w in weights.Values) total += w;
        double roll = Rng.NextDouble() * (total <= 0 ? 1 : total);
        double acc = 0;
        foreach (var kv in weights) { acc += kv.Value; if (roll <= acc) return kv.Key; }
        return weights.Keys.FirstOrDefault() ?? "common";
    }
}
