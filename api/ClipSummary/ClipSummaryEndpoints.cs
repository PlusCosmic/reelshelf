using Reelshelf.Auth;

namespace Reelshelf.ClipSummary;

public static class ClipSummaryEndpoints
{
    public static void MapClipSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin-only: summaries are search data, never shown to clip owners (ADR-0005), and every run is paid.
        RouteGroupBuilder group = app.MapGroup("clip-summary")
            .RequireAuthorization()
            .RequirePermission(Permissions.AdminUsers);

        group.MapGet("usage", GetUsage).WithName("GetClipSummaryUsage");
        group.MapPost("backfill", Backfill).WithName("BackfillClipSummary");
        group.MapPost("clips/{clipId:guid}/runs", QueueRun).WithName("QueueClipSummaryRun");
        group.MapGet("clips/{clipId:guid}/runs", GetRuns).WithName("GetClipSummaryRuns");
    }

    public static async Task<List<ClipSummaryUsage>> GetUsage(ClipSummaryService service)
    {
        return await service.GetUsageAsync();
    }

    public static async Task<ClipSummaryBackfillResponse> Backfill(
        BackfillClipSummaryRequest request,
        ClipSummaryService service)
    {
        return new ClipSummaryBackfillResponse(await service.BackfillAsync(request.ReasoningEffort));
    }

    public static async Task<ClipSummaryRun> QueueRun(
        Guid clipId,
        QueueClipSummaryRunRequest request,
        ClipSummaryService service)
    {
        return ClipSummaryRun.From(await service.QueueManualRunAsync(clipId, request.Provider, request.Model,
            request.ReasoningEffort));
    }

    public static async Task<List<ClipSummaryRun>> GetRuns(Guid clipId, ClipSummaryService service)
    {
        return (await service.GetRunsAsync(clipId)).Select(ClipSummaryRun.From).ToList();
    }
}

/// <summary>
/// Leave <c>Provider</c>, <c>Model</c> or <c>ReasoningEffort</c> empty to use the configured default. The effort
/// is one of the values from <c>GET /api/legend-detection/reasoning-efforts</c>.
/// </summary>
public sealed record QueueClipSummaryRunRequest(string? Provider, string? Model, string? ReasoningEffort);

/// <summary>Leave <c>ReasoningEffort</c> empty to use the configured default.</summary>
public sealed record BackfillClipSummaryRequest(string? ReasoningEffort);

public sealed record ClipSummaryBackfillResponse(int Queued);

/// <summary>
/// Totals for one provider, model, reasoning effort and prompt version. A null <c>ReasoningEffort</c> is the
/// model's default. Cached input tokens are part of the input total; embedding tokens are the embedding model's.
/// </summary>
public sealed record ClipSummaryUsage(
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
    long EmbeddingTokens,
    int? AverageDurationMs);

/// <summary>One summary attempt. The embedding itself is not sent; <c>HasEmbedding</c> says whether it is stored.</summary>
public sealed record ClipSummaryRun(
    Guid Id,
    Guid ClipId,
    Guid SourceTranscriptionRunId,
    string Trigger,
    string Provider,
    string Model,
    string? ReasoningEffort,
    string? PromptVersion,
    string Status,
    int Attempts,
    string? Description,
    List<string> MoodTags,
    List<string> Quotes,
    List<string> People,
    bool HasEmbedding,
    string EmbeddingModel,
    string? RawResponse,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    int? EmbeddingTokens,
    int? DurationMs,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ClipSummaryRun From(ClipSummaryStatements.ClipSummaryRunRow row)
    {
        return new ClipSummaryRun(
            row.Id,
            row.ClipId,
            row.SourceTranscriptionRunId,
            row.Trigger,
            row.Provider,
            row.Model,
            row.ReasoningEffort,
            row.PromptVersion,
            row.Status,
            row.Attempts,
            row.Description,
            row.MoodTags?.ToList() ?? [],
            row.Quotes is null ? [] : ClipSummaryResult.ParseQuotes(row.Quotes),
            row.People?.ToList() ?? [],
            row.HasEmbedding,
            row.EmbeddingModel,
            row.RawResponse,
            row.InputTokens,
            row.CachedInputTokens,
            row.OutputTokens,
            row.EmbeddingTokens,
            row.DurationMs,
            row.Error,
            row.CreatedAt,
            row.CompletedAt);
    }
}
