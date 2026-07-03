using ChaosCommander.Functions.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChaosCommander.Functions;

/// <summary>
/// Ежедневные/еженедельные задачи + входные награды (Phase 4). Конфиг — Title Data "taskConfig".
/// Прогресс/стрик/клеймы — UserReadOnlyData "playerProgress" (пишет только сервер), сброс по
/// серверному UTC (dayIndex/weekIndex). ЗАГОТОВКА.
/// </summary>
public static class Daily
{
    public class TaskState
    {
        public string Id = ""; public string Type = ""; public int Progress; public int Target;
        public bool Claimable; public bool Claimed; public RewardBundle Reward { get; set; } = new();
    }
    public class LoginRewardState { public int StreakDay; public bool Available; public RewardBundle Today { get; set; } = new(); }
    public class DailyStateResponse
    {
        public string ServerTimeUtc = ""; public int DailyResetHourUtc; public int WeeklyResetDay; public int WeeklyResetHourUtc;
        public LoginRewardState Login { get; set; } = new();
        public List<TaskState> Daily { get; set; } = new();
        public List<TaskState> Weekly { get; set; } = new();
    }

    [Function("GetDailyState")]
    public static async Task<HttpResponseData> GetState(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        // TODO(Phase 4): загрузить taskConfig + playerProgress; вычислить dayIndex/weekIndex по UTC;
        //  сбросить дневные/недельные при смене индекса; отдать claimable/claimed + login-статус.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, new DailyStateResponse { ServerTimeUtc = DateTime.UtcNow.ToString("o") });
    }

    [Function("ClaimLoginReward")]
    public static async Task<HttpResponseData> ClaimLogin(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<object>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new RewardResponse { Success = false, Reason = "not_implemented" };
        // TODO(Phase 4): проверить, что lastLoginDayIndex < сегодня → продвинуть стрик → GrantRewardAsync
        //  награду дня из loginRewards → записать прогресс → resp.Wallet.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }

    [Function("ClaimTask")]
    public static async Task<HttpResponseData> ClaimTask(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ClaimTaskRequest>(await FunctionHttp.ReadBodyAsync(req));
        var resp = new RewardResponse { Success = false, Reason = "not_implemented" };
        // TODO(Phase 4): найти задачу по TaskId; проверить progress>=target && !claimed → GrantRewardAsync → claimed=true.
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, resp);
    }

    [Function("ReportTaskProgress")]
    public static async Task<HttpResponseData> Report(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var ctx = PlayFabServer.ParseContext<ReportProgressRequest>(await FunctionHttp.ReadBodyAsync(req));
        // TODO(Phase 4): инкрементить прогресс всех daily/weekly задач c совпадающим Type (клэмп по target).
        // ВНИМАНИЕ: в P2P «win_games» self-report — хардить позже (валидация исхода матча).
        _ = ctx;
        return await FunctionHttp.JsonAsync(req, new BackendResult { Success = true });
    }
}
