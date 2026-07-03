using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace ChaosCommander.Functions.Shared;

/// <summary>Утилиты HTTP: чтение тела + JSON-ответ (то, что вернётся в ExecuteFunctionResult.FunctionResult).</summary>
public static class FunctionHttp
{
    public static async Task<string> ReadBodyAsync(HttpRequestData req)
    {
        using var reader = new StreamReader(req.Body);
        return await reader.ReadToEndAsync();
    }

    public static async Task<HttpResponseData> JsonAsync(HttpRequestData req, object payload)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await res.WriteStringAsync(JsonConvert.SerializeObject(payload));
        return res;
    }
}
