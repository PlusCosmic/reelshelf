using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;

namespace Reelshelf.ApexLegends.LegendDetection;

public static class LegendDetectionEndpoints
{
    public static void MapLegendDetectionEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin-only while detection is being trialled: every run is a paid model call.
        RouteGroupBuilder group = app.MapGroup("legend-detection")
            .RequireAuthorization()
            .RequirePermission(Permissions.AdminUsers);

        group.MapGet("clips", GetClipsForReview).WithName("GetLegendDetectionReviewClips");
        group.MapGet("usage", GetUsage).WithName("GetLegendDetectionUsage");
        group.MapPost("backfill", Backfill).WithName("BackfillLegendDetection");
        group.MapPost("archive", ArchiveAllRuns).WithName("ArchiveLegendDetectionRuns");
        group.MapPost("clips/{clipId:guid}/runs", QueueRun).WithName("QueueLegendDetectionRun");
        group.MapGet("clips/{clipId:guid}/runs", GetRuns).WithName("GetLegendDetectionRuns");
        group.MapPut("clips/{clipId:guid}/label", SetLabel).WithName("SetLegendDetectionLabel");
        group.MapDelete("clips/{clipId:guid}/label", DeleteLabel).WithName("DeleteLegendDetectionLabel");
        group.MapGet("legends", GetLegends).WithName("GetLegendDetectionLegends");
        group.MapGet("reasoning-efforts", GetReasoningEfforts).WithName("GetLegendDetectionReasoningEfforts");
    }

    public static async Task<List<LegendDetectionReviewClip>> GetClipsForReview(LegendDetectionService service)
    {
        return await service.GetClipsForReviewAsync();
    }

    public static async Task<List<LegendDetectionUsage>> GetUsage(LegendDetectionService service)
    {
        return await service.GetUsageAsync();
    }

    public static async Task<LegendDetectionBackfillResponse> Backfill(
        BackfillLegendDetectionRequest request,
        LegendDetectionService service)
    {
        return new LegendDetectionBackfillResponse(await service.BackfillAsync(request.ReasoningEffort));
    }

    public static async Task<LegendDetectionArchiveResponse> ArchiveAllRuns(LegendDetectionService service)
    {
        return new LegendDetectionArchiveResponse(await service.ArchiveAllRunsAsync());
    }

    public static async Task<LegendDetectionRun> QueueRun(
        Guid clipId,
        QueueLegendDetectionRunRequest request,
        LegendDetectionService service)
    {
        return LegendDetectionRun.From(await service.QueueManualRunAsync(clipId, request.Provider, request.Model,
            request.ReasoningEffort));
    }

    public static async Task<NoContent> SetLabel(
        Guid clipId,
        SetLegendDetectionLabelRequest request,
        AuthenticatedUser user,
        LegendDetectionService service)
    {
        await service.SetLabelAsync(clipId, request.PlayerLegend, request.TeammateLegends, user.Id);
        return TypedResults.NoContent();
    }

    public static async Task<NoContent> DeleteLabel(Guid clipId, LegendDetectionService service)
    {
        await service.DeleteLabelAsync(clipId);
        return TypedResults.NoContent();
    }

    /// <summary>The legends on the reference sheet, in the spelling labels and results use.</summary>
    public static IReadOnlyList<string> GetLegends()
    {
        return ApexLegendNames.All;
    }

    /// <summary>The reasoning efforts a run can ask for; not every model accepts every one.</summary>
    public static IReadOnlyList<string> GetReasoningEfforts()
    {
        return LegendReasoningEffort.All;
    }

    public static async Task<List<LegendDetectionRun>> GetRuns(Guid clipId, LegendDetectionService service)
    {
        return (await service.GetRunsAsync(clipId)).Select(LegendDetectionRun.From).ToList();
    }
}

/// <summary>
/// Leave <c>Provider</c>, <c>Model</c> or <c>ReasoningEffort</c> empty to use the configured default. The effort
/// is one of the values from <c>GET /api/legend-detection/reasoning-efforts</c>.
/// </summary>
public sealed record QueueLegendDetectionRunRequest(string? Provider, string? Model, string? ReasoningEffort);

/// <summary>Leave <c>ReasoningEffort</c> empty to use the configured default.</summary>
public sealed record BackfillLegendDetectionRequest(string? ReasoningEffort);

public sealed record LegendDetectionBackfillResponse(int Queued);

public sealed record LegendDetectionArchiveResponse(int Archived);

/// <summary>A null <c>PlayerLegend</c> means the clip owner's legend can't be identified from the clip.</summary>
public sealed record SetLegendDetectionLabelRequest(string? PlayerLegend, List<string> TeammateLegends);

/// <summary>The legends actually in a clip, as set by an admin. Teammates are unordered.</summary>
public sealed record LegendDetectionLabel(string? PlayerLegend, List<string> TeammateLegends, DateTimeOffset LabelledAt);

/// <summary>A clip to check a detection against: the frames the model is sent and a player for the full clip.</summary>
public sealed record LegendDetectionReviewClip(
    Guid ClipId,
    string Title,
    string OwnerName,
    DateTimeOffset CreatedAt,
    int? LengthSeconds,
    string EmbedUrl,
    List<string> FrameUrls,
    LegendDetectionRun? LatestRun,
    int RunCount,
    LegendDetectionLabel? Label);

/// <summary>
/// Totals for one provider, model, reasoning effort and prompt version; <c>ArchivedAt</c> is set for a batch of
/// archived runs. A null <c>ReasoningEffort</c> is the model's default.
/// Cached input tokens are part of the input total. <c>Labelled</c> counts succeeded runs on labelled clips,
/// which the player-correct and confident-mistake counts are out of; <c>SquadLabelled</c> is the subset whose
/// prompt asked for teammates, which <c>TeammatesCorrect</c> is out of.
/// </summary>
public sealed record LegendDetectionUsage(
    string Provider,
    string Model,
    string? ReasoningEffort,
    string? PromptVersion,
    int Succeeded,
    int Failed,
    int Queued,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    int? AverageDurationMs,
    int Labelled,
    int PlayerCorrect,
    int SquadLabelled,
    int TeammatesCorrect,
    int ConfidentMistakes,
    DateTimeOffset? ArchivedAt);

public sealed record LegendDetectionRun(
    Guid Id,
    Guid ClipId,
    string Trigger,
    string Provider,
    string Model,
    string? ReasoningEffort,
    string? PromptVersion,
    string Status,
    int Attempts,
    int? FrameCount,
    bool? HudDetected,
    string? PlayerLegend,
    float? PlayerLegendConfidence,
    string? PlayerName,
    float? PlayerNameConfidence,
    List<DetectedTeammate>? Teammates,
    string? RawResponse,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    int? DurationMs,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static LegendDetectionRun From(LegendDetectionStatements.LegendDetectionRunRow row)
    {
        return new LegendDetectionRun(
            row.Id,
            row.ClipId,
            row.Trigger,
            row.Provider,
            row.Model,
            row.ReasoningEffort,
            row.PromptVersion,
            row.Status,
            row.Attempts,
            row.FrameCount,
            row.HudDetected,
            row.PlayerLegend,
            row.PlayerLegendConfidence,
            row.PlayerName,
            row.PlayerNameConfidence,
            row.Teammates is null ? null : LegendDetectionResult.ParseTeammates(row.Teammates),
            row.RawResponse,
            row.InputTokens,
            row.CachedInputTokens,
            row.OutputTokens,
            row.DurationMs,
            row.Error,
            row.CreatedAt,
            row.CompletedAt);
    }
}
