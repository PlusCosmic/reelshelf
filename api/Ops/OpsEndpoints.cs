using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.FeatureFlags;
using Reelshelf.Ops.Costs;

namespace Reelshelf.Ops;

/// <summary>
/// Read-only operations numbers for the monitor app: storage, the transcode queue, the AI queues and costs. Admins only
/// (<see cref="Permissions.AdminOps"/>), with the site cookie or the monitor app's device token, behind the <c>ops-monitor</c> flag.
/// </summary>
public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("admin/ops")
            .RequireAuthorization()
            .RequireAuthorization(AuthorizationPolicies.CookieOrMonitorDevice)
            .RequirePermission(Permissions.AdminOps)
            .RequireFeature(FeatureFlagKeys.OpsMonitor);

        group.MapGet("storage", GetStorage).WithName("GetOpsStorage");
        group.MapGet("transcoding", GetTranscoding).WithName("GetOpsTranscoding");
        group.MapGet("ai", GetAi).WithName("GetOpsAi");
        group.MapGet("costs", GetCosts).WithName("GetOpsCosts");
    }

    private static async Task<Ok<OpsStorageResponse>> GetStorage(OpsService ops, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetStorageAsync(cancellationToken));

    private static async Task<Ok<OpsTranscodingResponse>> GetTranscoding(OpsService ops,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetTranscodingAsync(cancellationToken));

    private static async Task<Ok<OpsAiResponse>> GetAi(OpsService ops, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetAiAsync(cancellationToken));

    /// <summary>Spend by provider, as the background refresh last read it (<see cref="OpsCosts"/>).</summary>
    private static Ok<OpsCostsResponse> GetCosts(OpsCosts costs) => TypedResults.Ok(costs.Snapshot());
}
