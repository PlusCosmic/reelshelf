using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Reelshelf.Auth;

namespace Reelshelf.Plans;

/// <summary>Going over the limit, for the owner (ADR-0007): which clips are over it, picking ones to keep, and exports.</summary>
public static class OverLimitEndpoints
{
    public static void MapOverLimitEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder me = app.MapGroup("me").RequireAuthorization();
        me.MapGet("/over-limit", GetOverLimit).WithName("GetMyOverLimit");
        me.MapGet("/exports", GetExports).WithName("GetMyExports");
        me.MapPost("/exports", StartExport).WithName("StartMyExport");
        // A plain link, never fetch: the browser follows the redirect to R2 without CORS on the bucket.
        me.MapGet("/exports/{exportId:guid}/parts/{index:int}", DownloadExportPart).WithName("DownloadMyExportPart");

        app.MapPut("clips/{clipId:guid}/keep", SetKept).WithName("SetClipKept").RequireAuthorization();
    }

    /// <summary>
    /// The clips the account's plan doesn't keep, and the ones the owner picked to keep, with when the grace ends.
    /// Empty when the account is within its limit.
    /// </summary>
    private static async Task<Ok<OverLimitResponse>> GetOverLimit(
        AuthenticatedUser user,
        AccountPlanService planService,
        IPlanEnforcementStore accounts,
        IOptionsMonitor<PlanOptions> options)
    {
        AccountPlan plan = await planService.GetPlan(user);
        OverLimitState state = await accounts.GetState(user.Id);
        (List<OwnedClip> kept, List<OwnedClip> over) =
            OverLimit.Split(await accounts.GetOwnedClips(user.Id), plan.Limits.MaxClips);
        return TypedResults.Ok(new OverLimitResponse(
            plan.Limits.MaxClips,
            state.OverLimitSince,
            state.OverLimitSince + options.CurrentValue.GracePeriod,
            over.Select(OverLimitClip.From).ToList(),
            kept.Where(clip => clip.KeptAt is not null).Select(OverLimitClip.From).ToList()));
    }

    /// <summary>
    /// Picks a clip to keep when the account is over its limit, or unpicks it. 409 when the owner has already picked as
    /// many clips as the plan keeps; 404 when the clip isn't theirs.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, Conflict<string>>> SetKept(
        Guid clipId,
        SetClipKeptRequest request,
        AuthenticatedUser user,
        AccountPlanService planService,
        IPlanEnforcementStore accounts)
    {
        if (request.Keep)
        {
            AccountPlan plan = await planService.GetPlan(user);
            List<OwnedClip> clips = await accounts.GetOwnedClips(user.Id);
            int picked = clips.Count(clip => clip.KeptAt is not null && clip.Id != clipId);
            if (plan.Limits.MaxClips is { } limit && picked >= limit)
            {
                return TypedResults.Conflict(
                    $"You've picked {limit:N0} clips, as many as your plan keeps. Unpick one first.");
            }
        }

        return await accounts.SetKept(user.Id, clipId, request.Keep) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Ok<List<ClipExportResponse>>> GetExports(
        AuthenticatedUser user,
        IClipExportStore exports)
    {
        return TypedResults.Ok((await exports.GetForUser(user.Id)).Select(ClipExportResponse.From).ToList());
    }

    /// <summary>
    /// Starts an export of the clips over the account's limit, to download before they're deleted. 409 while one is
    /// already being made, 400 when no clip is over the limit, 404 while exports are off (no R2).
    /// </summary>
    private static async Task<Results<Accepted<ClipExportResponse>, NotFound, BadRequest<string>, Conflict<string>>> StartExport(
        AuthenticatedUser user,
        AccountPlanService planService,
        IPlanEnforcementStore accounts,
        IClipExportStore exports,
        IExportStorage storage)
    {
        if (!storage.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        if ((await exports.GetForUser(user.Id)).Any(export => ClipExportStatuses.IsActive(export.Status)))
        {
            return TypedResults.Conflict("A download is already being made. It'll be ready soon.");
        }

        AccountPlan plan = await planService.GetPlan(user);
        (_, List<OwnedClip> over) = OverLimit.Split(await accounts.GetOwnedClips(user.Id), plan.Limits.MaxClips);
        if (over.Count == 0)
        {
            return TypedResults.BadRequest("None of your clips are over your plan's limit.");
        }

        Guid exportId = await exports.Create(user.Id, ClipExportReasons.Requested, over.Select(clip => clip.Id).ToList());
        ClipExport export = (await exports.Get(exportId))!;
        return TypedResults.Accepted($"/api/me/exports", ClipExportResponse.From(export));
    }

    /// <summary>A no-store redirect to a 10-minute download of one of the caller's ready export zips; 404 otherwise.</summary>
    private static async Task<Results<RedirectHttpResult, NotFound>> DownloadExportPart(
        Guid exportId,
        int index,
        AuthenticatedUser user,
        IClipExportStore exports,
        IExportStorage storage,
        HttpContext context)
    {
        ClipExport? export = await exports.Get(exportId);
        if (export is null || export.UserId != user.Id || export.Status != ClipExportStatuses.Ready ||
            !storage.IsConfigured || export.Parts.All(part => part.Index != index))
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";
        string url = storage.DownloadUrl(ExportParts.Key(export.UserId, export.Id, index), ExportParts.FileName(index));
        return TypedResults.Redirect(url);
    }
}

public sealed record SetClipKeptRequest(bool Keep);

public sealed record OverLimitClip(Guid Id, string? Title, DateTimeOffset CreatedAt, string? GameName, bool Picked)
{
    public static OverLimitClip From(OwnedClip clip) =>
        new(clip.Id, clip.Title, clip.CreatedAt, clip.GameName, clip.KeptAt is not null);
}

/// <summary>
/// The account's clips over its limit and the ones the owner picked to keep. <see cref="DeletesAt"/> is when the clips
/// over the limit are deleted, while a grace is running.
/// </summary>
public sealed record OverLimitResponse(
    int? ClipLimit,
    DateTimeOffset? OverLimitSince,
    DateTimeOffset? DeletesAt,
    List<OverLimitClip> Over,
    List<OverLimitClip> Picked);

public sealed record ClipExportPartResponse(int Index, long SizeBytes, int ClipCount);

public sealed record ClipExportResponse(
    Guid Id,
    string Reason,
    string Status,
    int ClipCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    List<ClipExportPartResponse> Parts)
{
    public static ClipExportResponse From(ClipExport export) => new(
        export.Id,
        export.Reason,
        export.Status,
        export.ClipIds.Count,
        export.CreatedAt,
        export.ExpiresAt,
        export.Parts.Select(part => new ClipExportPartResponse(part.Index, part.SizeBytes, part.ClipCount)).ToList());
}
