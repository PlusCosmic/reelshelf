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
        group.MapGet("evals", GetEvals).WithName("GetLegendEvals");
        group.MapPost("evals", CreateEval).WithName("CreateLegendEval");
        group.MapDelete("evals/{evalId:guid}", DeleteEval).WithName("DeleteLegendEval");
        group.MapGet("evals/{evalId:guid}/misses", GetEvalMisses).WithName("GetLegendEvalMisses");
        group.MapGet("prices", GetPrices).WithName("GetLegendModelPrices");
        group.MapPut("prices", SetPrice).WithName("SetLegendModelPrice");
        group.MapGet("clips/{clipId:guid}/runs", GetRuns).WithName("GetLegendDetectionRuns");
        group.MapPut("clips/{clipId:guid}/label", SetLabel).WithName("SetLegendDetectionLabel");
        group.MapDelete("clips/{clipId:guid}/label", DeleteLabel).WithName("DeleteLegendDetectionLabel");
        group.MapGet("baseline", GetBaseline).WithName("GetLegendDetectionBaseline");
        group.MapPut("labels", SetPlayerLabels).WithName("SetLegendDetectionPlayerLabels");
        group.MapPost("labels/clear", ClearLabels).WithName("ClearLegendDetectionLabels");
        group.MapGet("reference-sheet", GetReferenceSheet).WithName("GetLegendDetectionReferenceSheet");
        group.MapGet("legends", GetLegends).WithName("GetLegendDetectionLegends");
        group.MapGet("reasoning-efforts", GetReasoningEfforts).WithName("GetLegendDetectionReasoningEfforts");
        group.MapGet("providers", GetProviders).WithName("GetLegendDetectionProviders");
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

    public static async Task<List<LegendEval>> GetEvals(LegendDetectionService service)
    {
        return await service.GetEvalsAsync();
    }

    /// <summary>
    /// Runs a provider, model and reasoning effort on every labelled clip; the options work as in
    /// <see cref="QueueLegendDetectionRunRequest"/>.
    /// </summary>
    public static async Task<LegendEvalCreatedResponse> CreateEval(
        QueueLegendDetectionRunRequest request,
        AuthenticatedUser user,
        LegendDetectionService service)
    {
        return await service.CreateEvalAsync(request.Provider, request.Model, request.ReasoningEffort, user.Id);
    }

    /// <summary>Deletes an eval with its runs, including any still queued.</summary>
    public static async Task<NoContent> DeleteEval(Guid evalId, LegendDetectionService service)
    {
        await service.DeleteEvalAsync(evalId);
        return TypedResults.NoContent();
    }

    public static async Task<List<LegendEvalMiss>> GetEvalMisses(Guid evalId, LegendDetectionService service)
    {
        return await service.GetEvalMissesAsync(evalId);
    }

    public static async Task<List<LegendModelPrice>> GetPrices(LegendDetectionService service)
    {
        return await service.GetPricesAsync();
    }

    public static async Task<NoContent> SetPrice(LegendModelPrice request, LegendDetectionService service)
    {
        await service.SetPriceAsync(request);
        return TypedResults.NoContent();
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

    public static async Task<List<LegendBaselineClip>> GetBaseline(LegendDetectionService service)
    {
        return await service.GetBaselineAsync();
    }

    /// <summary>Sets the owner's legend on many clips at once, keeping any teammate legends already labelled.</summary>
    public static async Task<LegendDetectionLabelsSavedResponse> SetPlayerLabels(
        SetLegendDetectionPlayerLabelsRequest request,
        AuthenticatedUser user,
        LegendDetectionService service)
    {
        return new LegendDetectionLabelsSavedResponse(await service.SetPlayerLabelsAsync(request.Labels, user.Id));
    }

    /// <summary>Removes the labels of many clips at once, as the baseline page undoes a confirmed group.</summary>
    public static async Task<LegendDetectionLabelsClearedResponse> ClearLabels(
        ClearLegendDetectionLabelsRequest request,
        LegendDetectionService service)
    {
        return new LegendDetectionLabelsClearedResponse(await service.ClearLabelsAsync(request.ClipIds));
    }

    /// <summary>
    /// The reference sheet the model is sent: two rows of fourteen labelled portraits, in
    /// <see cref="ApexLegendNames.All"/> order, so the baseline page can show each legend's portrait.
    /// </summary>
    public static FileContentHttpResult GetReferenceSheet(LegendDetectionResources resources)
    {
        return TypedResults.File(resources.ReferenceSheet, "image/png");
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

    /// <summary>
    /// The providers with an API key, which a run can be queued against. <c>openai-decisions</c> is OpenAI's
    /// Decisions API: it defaults to its own model and takes no reasoning effort.
    /// </summary>
    public static IReadOnlyList<string> GetProviders(LegendDetectionService service)
    {
        return service.GetProviders();
    }

    public static async Task<List<LegendDetectionRun>> GetRuns(Guid clipId, LegendDetectionService service)
    {
        return (await service.GetRunsAsync(clipId)).Select(LegendDetectionRun.From).ToList();
    }
}

/// <summary>
/// Leave <c>Provider</c>, <c>Model</c> or <c>ReasoningEffort</c> empty to use the configured default. The provider
/// is one of <c>GET /api/legend-detection/providers</c>; a provider other than the configured one falls back to its
/// own default model, if it has one. The effort is one of the values from
/// <c>GET /api/legend-detection/reasoning-efforts</c>, and must be empty for a provider that takes none.
/// </summary>
public sealed record QueueLegendDetectionRunRequest(string? Provider, string? Model, string? ReasoningEffort);

/// <summary>Leave <c>ReasoningEffort</c> empty to use the configured default.</summary>
public sealed record BackfillLegendDetectionRequest(string? ReasoningEffort);

public sealed record LegendDetectionBackfillResponse(int Queued);

public sealed record LegendDetectionArchiveResponse(int Archived);

public sealed record LegendEvalCreatedResponse(Guid EvalId, int Queued);

/// <summary>
/// One eval's results. <c>Scored</c> counts finished runs on clips that are still labelled, which
/// <c>Correct</c> and <c>ConfidentMistakes</c> are out of; failed runs count as wrong. <c>CostUsd</c> is null
/// while any succeeded run has neither a billed cost nor a price for its model; <c>CostBilled</c> is true when
/// every succeeded run's cost came from the provider's bill. Latency is the model call of succeeded runs;
/// <c>WallSeconds</c> runs from the first run starting to the last finishing, once none is queued.
/// </summary>
public sealed record LegendEval(
    Guid Id,
    string Provider,
    string Model,
    string? ReasoningEffort,
    DateTimeOffset CreatedAt,
    int Runs,
    int Succeeded,
    int Failed,
    int Queued,
    int Scored,
    int Correct,
    int ConfidentMistakes,
    long InputTokens,
    long CachedInputTokens,
    long CacheWriteInputTokens,
    long OutputTokens,
    decimal? CostUsd,
    bool CostBilled,
    int? P50DurationMs,
    int? P95DurationMs,
    int? WallSeconds,
    List<string> PromptVersions)
{
    public static LegendEval From(LegendDetectionStatements.EvalRow row)
    {
        int? wallSeconds = row.Queued == 0 && row.FirstStartedAt is { } started && row.LastCompletedAt is { } finished
            ? (int)Math.Round((finished - started).TotalSeconds)
            : null;
        return new LegendEval(
            row.Id,
            row.Provider,
            row.Model,
            row.ReasoningEffort,
            row.CreatedAt,
            (int)row.Runs,
            (int)row.Succeeded,
            (int)row.Failed,
            (int)row.Queued,
            (int)row.Scored,
            (int)row.Correct,
            (int)row.ConfidentMistakes,
            row.InputTokens,
            row.CachedInputTokens,
            row.CacheWriteInputTokens,
            row.OutputTokens,
            row.Unpriced > 0 || row.Succeeded == 0 ? null : row.CostUsd,
            row.Succeeded > 0 && row.Billed >= row.Succeeded,
            row.P50DurationMs,
            row.P95DurationMs,
            wallSeconds,
            row.PromptVersions.ToList());
    }
}

/// <summary>
/// A clip an eval got wrong: a failed run (<c>Status</c> "failed", with its <c>Error</c>), or a legend that is not
/// the label. A null legend means the run, or the label, says the owner's legend can't be identified.
/// </summary>
public sealed record LegendEvalMiss(
    Guid RunId,
    Guid ClipId,
    string Title,
    List<string> FrameUrls,
    string Status,
    string? PlayerLegend,
    float? Confidence,
    bool? HudDetected,
    string? Error,
    string? LabelLegend);

/// <summary>A model's prices in US dollars per million tokens. Cached and cache-write prices fall back to input when null.</summary>
public sealed record LegendModelPrice(
    string Provider,
    string Model,
    decimal? InputPerMtok,
    decimal? CachedInputPerMtok,
    decimal? CacheWritePerMtok,
    decimal? OutputPerMtok);

/// <summary>A null <c>PlayerLegend</c> means the clip owner's legend can't be identified from the clip.</summary>
public sealed record SetLegendDetectionLabelRequest(string? PlayerLegend, List<string> TeammateLegends);

public sealed record SetLegendDetectionPlayerLabelsRequest(List<LegendPlayerLabel> Labels);

/// <summary>A null <c>PlayerLegend</c> means the clip owner's legend can't be identified from the clip.</summary>
public sealed record LegendPlayerLabel(Guid ClipId, string? PlayerLegend);

public sealed record LegendDetectionLabelsSavedResponse(int Saved);

public sealed record ClearLegendDetectionLabelsRequest(List<Guid> ClipIds);

public sealed record LegendDetectionLabelsClearedResponse(int Cleared);

/// <summary>
/// A clip on the baseline page. <c>Label</c> is the saved owner's legend, if the clip has been labelled;
/// <c>Result</c> is the latest succeeded run, which Reelshelf currently uses for the clip.
/// </summary>
public sealed record LegendBaselineClip(
    Guid ClipId,
    string Title,
    string OwnerName,
    DateTimeOffset CreatedAt,
    List<string> FrameUrls,
    LegendBaselineLabel? Label,
    LegendBaselineResult? Result);

/// <summary>A null <c>PlayerLegend</c> means the owner's legend can't be identified from the clip.</summary>
public sealed record LegendBaselineLabel(string? PlayerLegend, DateTimeOffset LabelledAt);

public sealed record LegendBaselineResult(
    string? PlayerLegend,
    float? Confidence,
    string Provider,
    string Model,
    bool? HudDetected);

/// <summary>The legends actually in a clip, as set by an admin. Teammates are unordered.</summary>
public sealed record LegendDetectionLabel(string? PlayerLegend, List<string> TeammateLegends, DateTimeOffset LabelledAt);

/// <summary>A clip to check a detection against: the frames the model is sent and a player for the full clip.</summary>
public sealed record LegendDetectionReviewClip(
    Guid ClipId,
    string Title,
    string OwnerName,
    DateTimeOffset CreatedAt,
    int? LengthSeconds,
    string PlaybackUrl,
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
