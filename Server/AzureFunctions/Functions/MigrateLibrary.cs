using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ServerModels;

namespace ChaosCommander.Functions;

/// <summary>
/// Одноразовая миграция старой библиотеки (UserData "player_library", client-authoritative JSON)
/// в инвентарь Economy v2. Идемпотентна: флаг "library_migrated" в UserReadOnlyData (пишет только
/// сервер). Безопасно звать при каждом логине — повторно ничего не выдаст.
/// </summary>
public static class MigrateLibrary
{
    const string LegacyKey = "player_library";
    const string MigratedFlag = "library_migrated";

    // Формат старого блоба: { "Cards": [ { "ExpansionId": "...", "CardId": 1, "Count": 4 }, ... ] }
    class OwnedList { public List<OwnedCard> Cards = new(); }
    class OwnedCard { public string ExpansionId = ""; public int CardId; public int Count; }

    [Function("MigrateLibrary")]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        var playFabId = ctx.MasterPlayerAccountId;
        var result = new MigrateResult { Migrated = false, CardsGranted = 0 };

        if (string.IsNullOrEmpty(playFabId))
            return await FunctionHttp.JsonAsync(req, result);

        var server = new PlayFabServerInstanceAPI(PlayFabServer.TitleSettings());

        // 1) Уже мигрировано? (readonly data — источник истины сервера)
        var roData = await server.GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayFabId = playFabId });
        if (roData.Result?.Data != null && roData.Result.Data.ContainsKey(MigratedFlag))
            return await FunctionHttp.JsonAsync(req, result);

        // 2) Прочитать старую библиотеку
        var userData = await server.GetUserDataAsync(new GetUserDataRequest
        {
            PlayFabId = playFabId,
            Keys = new List<string> { LegacyKey },
        });

        int granted = 0;
        if (userData.Result?.Data != null && userData.Result.Data.TryGetValue(LegacyKey, out var record))
        {
            OwnedList? owned = null;
            try { owned = JsonConvert.DeserializeObject<OwnedList>(record.Value); } catch { /* битый блоб — пропуск */ }

            if (owned?.Cards != null)
            {
                var entity = PlayFabServer.EntityOf(ctx);
                foreach (var c in owned.Cards)
                {
                    if (string.IsNullOrEmpty(c.ExpansionId) || c.Count <= 0) continue;
                    string itemId = $"{c.ExpansionId}_{c.CardId}";
                    await PlayFabServer.GrantItemAsync(entity, itemId, c.Count);
                    granted += c.Count;
                }
            }
        }

        // 3) Пометить мигрированным (даже если карт не было — чтобы не сканировать повторно)
        await server.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
        {
            PlayFabId = playFabId,
            Data = new Dictionary<string, string> { { MigratedFlag, DateTime.UtcNow.ToString("o") } },
        });

        result.Migrated = true;
        result.CardsGranted = granted;
        return await FunctionHttp.JsonAsync(req, result);
    }
}
