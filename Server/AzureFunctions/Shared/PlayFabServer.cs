using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.AuthenticationModels;
using PlayFab.EconomyModels;
using EntityKey = PlayFab.EconomyModels.EntityKey;

namespace ChaosCommander.Functions.Shared;

/// <summary>
/// Общий серверный слой: разбор контекста PlayFab (кто вызвал), настройки title (секрет из env),
/// авторитетные вызовы Economy v2 и единый GrantReward. Все функции получают тело
/// FunctionExecutionContext и работают ТОЛЬКО с CallerEntityProfile (клиенту не доверяем).
/// </summary>
public static class PlayFabServer
{
    public static string TitleId => Environment.GetEnvironmentVariable("PLAYFAB_TITLE_ID")
        ?? throw new InvalidOperationException("PLAYFAB_TITLE_ID not set");
    public static string SecretKey => Environment.GetEnvironmentVariable("PLAYFAB_DEV_SECRET_KEY")
        ?? throw new InvalidOperationException("PLAYFAB_DEV_SECRET_KEY not set");

    // Item Id валют в каталоге (заполнить после создания валют в Game Manager).
    public static string GoldItemId => Environment.GetEnvironmentVariable("PLAYFAB_GOLD_ITEM_ID") ?? "";
    public static string GemsItemId => Environment.GetEnvironmentVariable("PLAYFAB_GEMS_ITEM_ID") ?? "";

    public const string GoldCode = "GD";
    public const string GemsCode = "GM";

    /// <summary>Title-авторитетные настройки (dev secret). Все мутации инвентаря идут так.</summary>
    public static PlayFabApiSettings TitleSettings() => new()
    {
        TitleId = TitleId,
        DeveloperSecretKey = SecretKey,
    };

    // ── Разбор контекста вызова ────────────────────────────────────────────

    /// <summary>Распарсить тело запроса (FunctionExecutionContext) в контекст + типизированный аргумент.</summary>
    public static CallContext<T> ParseContext<T>(string requestBody)
    {
        var root = JObject.Parse(string.IsNullOrEmpty(requestBody) ? "{}" : requestBody);
        var caller = root["CallerEntityProfile"];
        var entity = caller?["Entity"];
        var lineage = caller?["Lineage"];

        var arg = root["FunctionArgument"];
        T typedArg = arg != null ? arg.ToObject<T>()! : default!;

        return new CallContext<T>
        {
            EntityId   = entity?["Id"]?.ToString() ?? "",
            EntityType = entity?["Type"]?.ToString() ?? "title_player_account",
            MasterPlayerAccountId = lineage?["MasterPlayerAccountId"]?.ToString() ?? "",
            TitlePlayerAccountId  = lineage?["TitlePlayerAccountId"]?.ToString() ?? "",
            Argument = typedArg,
        };
    }

    public static EntityKey EntityOf<T>(CallContext<T> ctx) => new() { Id = ctx.EntityId, Type = ctx.EntityType };

    // ── Инвентарь / выдача ──────────────────────────────────────────────────

    /// <summary>Выдать предмет каталога игроку (карта/бустер/аватар).</summary>
    public static async Task GrantItemAsync(EntityKey player, string itemId, int amount)
    {
        var economy = new PlayFabEconomyInstanceAPI(TitleSettings());
        var res = await economy.AddInventoryItemsAsync(new AddInventoryItemsRequest
        {
            Entity = player,
            Amount = amount,
            Item = new InventoryItemReference { Id = itemId },
        });
        if (res.Error != null)
            throw new PlayFabException($"AddInventoryItems({itemId} x{amount}) failed: {res.Error.GenerateErrorReport()}");
    }

    /// <summary>Начислить валюту по коду ("GD"/"GM").</summary>
    public static async Task GrantCurrencyAsync(EntityKey player, string code, int amount)
    {
        string itemId = code == GemsCode ? GemsItemId : GoldItemId;
        if (string.IsNullOrEmpty(itemId))
            throw new InvalidOperationException($"Currency item id for '{code}' not configured (PLAYFAB_*_ITEM_ID).");
        await GrantItemAsync(player, itemId, amount);
    }

    /// <summary>Списать валюту. Бросает при недостатке средств (через SubtractInventoryItems).</summary>
    public static async Task<bool> TrySpendCurrencyAsync(EntityKey player, string code, int amount)
    {
        string itemId = code == GemsCode ? GemsItemId : GoldItemId;
        var economy = new PlayFabEconomyInstanceAPI(TitleSettings());
        var res = await economy.SubtractInventoryItemsAsync(new SubtractInventoryItemsRequest
        {
            Entity = player,
            Amount = amount,
            Item = new InventoryItemReference { Id = itemId },
        });
        return res.Error == null;
    }

    /// <summary>Выдать целый RewardBundle (валюты + карты + бустеры + аватары).</summary>
    public static async Task GrantRewardAsync(EntityKey player, RewardBundle reward)
    {
        if (reward == null) return;
        foreach (var c in reward.Currencies ?? new()) await GrantCurrencyAsync(player, c.Code, c.Amount);
        foreach (var card in reward.Cards ?? new())    await GrantItemAsync(player, card.ItemId, card.Amount);
        foreach (var b in reward.Boosters ?? new())     await GrantItemAsync(player, b, 1);
        foreach (var a in reward.Avatars ?? new())       await GrantItemAsync(player, a, 1);
    }

    /// <summary>Прочитать все валюты игрока → список для клиента (Wallet).</summary>
    public static async Task<List<CurrencyAmount>> ReadWalletAsync(EntityKey player)
    {
        var economy = new PlayFabEconomyInstanceAPI(TitleSettings());
        var wallet = new List<CurrencyAmount>();
        string? token = null;
        do
        {
            var res = await economy.GetInventoryItemsAsync(new GetInventoryItemsRequest
            {
                Entity = player, Count = 50, ContinuationToken = token,
            });
            if (res.Error != null) break;
            foreach (var item in res.Result.Items ?? new())
            {
                if (item.Type != "currency") continue;
                string? code = item.Id == GemsItemId ? GemsCode : item.Id == GoldItemId ? GoldCode : null;
                if (code != null) wallet.Add(new CurrencyAmount { Code = code, Amount = item.Amount ?? 0 });
            }
            token = res.Result.ContinuationToken;
        } while (!string.IsNullOrEmpty(token));
        return wallet;
    }

    // ── Title Data ───────────────────────────────────────────────────────────

    /// <summary>Прочитать ключ Title Data (конфиги бустеров/рынка/задач).</summary>
    public static async Task<string?> GetTitleDataAsync(string key)
    {
        var server = new PlayFabServerInstanceAPI(TitleSettings());
        var res = await server.GetTitleDataAsync(new PlayFab.ServerModels.GetTitleDataRequest
        {
            Keys = new List<string> { key },
        });
        if (res.Error != null || res.Result.Data == null) return null;
        return res.Result.Data.TryGetValue(key, out var v) ? v : null;
    }
}

/// <summary>Контекст вызова: кто вызвал + типизированный аргумент.</summary>
public class CallContext<T>
{
    public string EntityId = "";
    public string EntityType = "";
    public string MasterPlayerAccountId = "";
    public string TitlePlayerAccountId = "";
    public T Argument = default!;
}
