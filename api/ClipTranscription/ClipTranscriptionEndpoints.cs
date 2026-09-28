using Reelshelf.Auth;

namespace Reelshelf.ClipTranscription;

public static class ClipTranscriptionEndpoints
{
    public static void MapClipTranscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin-only: transcripts are search data, never shown to clip owners (ADR-0005), and every run is paid.
        RouteGroupBuilder group = app.MapGroup("clip-transcription")
            .RequireAuthorization()
            .RequirePermission(Permissions.AdminUsers);

        group.MapGet("clips", GetClipsForReview).WithName("GetClipTranscriptionReviewClips");
        group.MapGet("usage", GetUsage).WithName("GetClipTranscriptionUsage");
        group.MapPost("backfill", Backfill).WithName("BackfillClipTranscription");
        group.MapPost("retry-empty", RetryEmpty).WithName("RetryEmptyClipTranscriptions");
        group.MapPost("clips/{clipId:guid}/runs", QueueRun).WithName("QueueClipTranscriptionRun");
        group.MapGet("clips/{clipId:guid}/runs", GetRuns).WithName("GetClipTranscriptionRuns");
    }

    public static async Task<List<ClipTranscriptionReviewClip>> GetClipsForReview(ClipTranscriptionService service)
    {
        return await service.GetClipsForReviewAsync();
    }

    public static async Task<List<ClipTranscriptionUsage>> GetUsage(ClipTranscriptionService service)
    {
        return await service.GetUsageAsync();
    }

    public static async Task<ClipTranscriptionBackfillResponse> Backfill(ClipTranscriptionService service)
    {
        return new ClipTranscriptionBackfillResponse(await service.BackfillAsync());
    }

    public static async Task<ClipTranscriptionBackfillResponse> RetryEmpty(ClipTranscriptionService service)
    {
        return new ClipTranscriptionBackfillResponse(await service.RetryEmptyAsync());
    }

    public static async Task<ClipTranscriptionRun> QueueRun(
        Guid clipId,
        QueueClipTranscriptionRunRequest request,
        ClipTranscriptionService service)
    {
        return ClipTranscriptionRun.From(await service.QueueManualRunAsync(clipId, request.Model));
    }

    public static async Task<List<ClipTranscriptionRun>> GetRuns(Guid clipId, ClipTranscriptionService service)
    {
        return (await service.GetRunsAsync(clipId)).Select(ClipTranscriptionRun.From).ToList();
    }
}

/// <summary>Leave <c>Model</c> empty to use the configured default.</summary>
public sealed record QueueClipTranscriptionRunRequest(string? Model);

public sealed record ClipTranscriptionBackfillResponse(int Queued);

/// <summary>A clip to check a transcript against, with a player for the full clip.</summary>
public sealed record ClipTranscriptionReviewClip(
    Guid ClipId,
    string Title,
    string OwnerName,
    string GameName,
    string GameSlug,
    DateTimeOffset CreatedAt,
    int? LengthSeconds,
    string EmbedUrl,
    string ThumbnailUrl,
    ClipTranscriptionRun? LatestRun,
    int RunCount);

/// <summary>
/// Totals for one model and prompt version. <c>WithoutAudio</c> counts clips with no audio track, which cost
/// nothing; <c>WithoutSpeech</c> counts clips whose audio came back with no words. The cost is an estimate from
/// the model's price in <c>ClipTranscription:CostPerMinuteUsd</c>, null for a model with no price listed.
/// </summary>
public sealed record ClipTranscriptionUsage(
    string Model,
    string? PromptVersion,
    int Succeeded,
    int Failed,
    int Queued,
    int WithoutAudio,
    int WithoutSpeech,
    double AudioMinutes,
    decimal? EstimatedCostUsd,
    long InputTokens,
    long OutputTokens,
    int? AverageDurationMs);

public sealed record ClipTranscriptionRun(
    Guid Id,
    Guid ClipId,
    string Trigger,
    string Model,
    string? PromptVersion,
    string? Prompt,
    List<string> Keywords,
    string Status,
    int Attempts,
    bool? HasAudio,
    float? AudioSeconds,
    int? AudioBytes,
    string? Transcript,
    List<string> Languages,
    int? InputTokens,
    int? OutputTokens,
    int? DurationMs,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ClipTranscriptionRun From(ClipTranscriptionStatements.ClipTranscriptionRunRow row)
    {
        return new ClipTranscriptionRun(
            row.Id,
            row.ClipId,
            row.Trigger,
            row.Model,
            row.PromptVersion,
            row.Prompt,
            row.Keywords?.ToList() ?? [],
            row.Status,
            row.Attempts,
            row.HasAudio,
            row.AudioSeconds,
            row.AudioBytes,
            row.Transcript,
            row.Languages?.ToList() ?? [],
            row.InputTokens,
            row.OutputTokens,
            row.DurationMs,
            row.Error,
            row.CreatedAt,
            row.CompletedAt);
    }
}
