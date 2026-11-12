using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.FeatureFlags;

namespace Reelshelf.Ops;

/// <summary>
/// Read-only operations numbers for the monitor app: storage, the transcode queue and the AI queues. Admins only
/// (<see cref="Permissions.AdminOps"/>), with the site cookie or a device token, behind the <c>ops-monitor</c> flag.
/// </summary>
public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("admin/ops")
            .RequireAuthorization()
            .RequireAuthorization(AuthorizationPolicies.CookieOrDevice)
            .RequirePermission(Permissions.AdminOps)
            .RequireFeature(FeatureFlagKeys.OpsMonitor);

        group.MapGet("storage", GetStorage).WithName("GetOpsStorage");
        group.MapGet("transcoding", GetTranscoding).WithName("GetOpsTranscoding");
        group.MapGet("ai", GetAi).WithName("GetOpsAi");
    }

    private static async Task<Ok<OpsStorageResponse>> GetStorage(OpsService ops, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetStorageAsync(cancellationToken));

    private static async Task<Ok<OpsTranscodingResponse>> GetTranscoding(OpsService ops,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetTranscodingAsync(cancellationToken));

    private static async Task<Ok<OpsAiResponse>> GetAi(OpsService ops, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ops.GetAiAsync(cancellationToken));
}
