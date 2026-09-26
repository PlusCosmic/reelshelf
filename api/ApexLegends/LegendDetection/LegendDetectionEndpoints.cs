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

        group.MapPost("backfill", Backfill).WithName("BackfillLegendDetection");
        group.MapPost("clips/{clipId:guid}/runs", QueueRun).WithName("QueueLegendDetectionRun");
        group.MapGet("clips/{clipId:guid}/runs", GetRuns).WithName("GetLegendDetectionRuns");
    }

    public static async Task<LegendDetectionBackfillResponse> Backfill(LegendDetectionService service)
    {
        return new LegendDetectionBackfillResponse(await service.BackfillAsync());
    }

    public static async Task<LegendDetectionRun> QueueRun(
        Guid clipId,
        QueueLegendDetectionRunRequest request,
        LegendDetectionService service)
    {
        return LegendDetectionRun.From(await service.QueueManualRunAsync(clipId, request.Provider, request.Model));
    }

    public static async Task<List<LegendDetectionRun>> GetRuns(Guid clipId, LegendDetectionService service)
    {
        return (await service.GetRunsAsync(clipId)).Select(LegendDetectionRun.From).ToList();
    }
}

/// <summary>Leave <c>Provider</c> or <c>Model</c> empty to use the configured default.</summary>
public sealed record QueueLegendDetectionRunRequest(string? Provider, string? Model);

public sealed record LegendDetectionBackfillResponse(int Queued);

public sealed record LegendDetectionRun(
    Guid Id,
    Guid ClipId,
    string Trigger,
    string Provider,
    string Model,
    string? PromptVersion,
    string Status,
    int Attempts,
    int? FrameCount,
    bool? HudDetected,
    string? PlayerLegend,
    float? PlayerLegendConfidence,
    string? PlayerName,
    float? PlayerNameConfidence,
    List<DetectedTeammate> Teammates,
    string? RawResponse,
    int? InputTokens,
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
            row.PromptVersion,
            row.Status,
            row.Attempts,
            row.FrameCount,
            row.HudDetected,
            row.PlayerLegend,
            row.PlayerLegendConfidence,
            row.PlayerName,
            row.PlayerNameConfidence,
            row.Teammates is null ? [] : LegendDetectionResult.ParseTeammates(row.Teammates),
            row.RawResponse,
            row.InputTokens,
            row.OutputTokens,
            row.DurationMs,
            row.Error,
            row.CreatedAt,
            row.CompletedAt);
    }
}
