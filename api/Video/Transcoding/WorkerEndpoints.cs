using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Reelshelf.Video.Transcoding;

/// <summary>
/// A job for a worker. <paramref name="LeaseId"/> goes with every report on it. <paramref name="SourceUrl"/> is a
/// presigned GET for the job's input and <paramref name="UploadUrls"/> a presigned PUT for each output, by file name
/// (<c>av1.mp4</c>, <c>thumbnail.jpg</c>, <c>thumbnail_1.jpg</c>..<c>thumbnail_5.jpg</c>, <c>preview.mp4</c>); each PUT
/// signs <c>Content-Type</c> and <c>Cache-Control</c>, which the worker must send as given in
/// docs/video-pipeline/workers-and-jobs.md. Both are null while R2 is not configured.
/// </summary>
public sealed record WorkerJobResponse(
    Guid RunId,
    Guid LeaseId,
    Guid ClipId,
    string Type,
    int VideoVersion,
    int Attempt,
    JsonElement Input,
    string? SourceUrl,
    IReadOnlyDictionary<string, string>? UploadUrls);

public sealed record WorkerProgressRequest(Guid LeaseId, int Percent, bool ThumbnailsReady = false);

/// <summary>What a finished job produced: its output keys (free-form, by name), the video's length and its size.</summary>
public sealed record WorkerCompleteRequest(Guid LeaseId, JsonElement? Outputs, double LengthSeconds, long SizeBytes);

public sealed record WorkerFailRequest(Guid LeaseId, string Error, bool Retryable);

/// <summary>
/// The transcode workers' API (docs/video-pipeline/workers-and-jobs.md). Workers authenticate with the shared
/// secret through <see cref="WorkerAuthentication.Policy"/>, never the site cookie, and every endpoint requires it.
/// The endpoints are left out of the OpenAPI document: the worker is not a client of the generated frontend API.
/// </summary>
public static class WorkerEndpoints
{
    /// <summary>The largest <c>outputs</c> object a completion may carry once serialized.</summary>
    public const int MaxOutputsBytes = 64 * 1024;

    public static void MapWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("worker");
        group.ExcludeFromDescription();

        group.MapPost("/claim", Claim)
            .RequireAuthorization(WorkerAuthentication.Policy)
            .RequireRateLimiting(RateLimitPolicies.Worker);
        group.MapPost("/{runId:guid}/progress", ReportProgress)
            .RequireAuthorization(WorkerAuthentication.Policy)
            .RequireRateLimiting(RateLimitPolicies.Worker);
        group.MapPost("/{runId:guid}/complete", Complete)
            .RequireAuthorization(WorkerAuthentication.Policy)
            .RequireRateLimiting(RateLimitPolicies.Worker);
        group.MapPost("/{runId:guid}/fail", Fail)
            .RequireAuthorization(WorkerAuthentication.Policy)
            .RequireRateLimiting(RateLimitPolicies.Worker);
    }

    /// <summary>The next job, or 204 when there is none and the worker should poll again later.</summary>
    private static async Task<Results<Ok<WorkerJobResponse>, NoContent>> Claim(TranscodeQueue queue)
    {
        ClaimedTranscodeRun? run = await queue.ClaimAsync();
        if (run is null)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Ok(new WorkerJobResponse(run.RunId, run.LeaseId, run.ClipId, run.Type, run.VideoVersion,
            run.Attempt, run.Input, run.SourceUrl, run.UploadUrls));
    }

    private static async Task<Results<NoContent, BadRequest<string>, NotFound, Conflict<string>>> ReportProgress(
        Guid runId,
        WorkerProgressRequest request,
        TranscodeQueue queue)
    {
        if (request.LeaseId == Guid.Empty)
        {
            return TypedResults.BadRequest("lease_id is required");
        }

        if (request.Percent is < 0 or > 100)
        {
            return TypedResults.BadRequest("percent must be between 0 and 100");
        }

        return ToResult(await queue.ReportProgressAsync(runId, request.LeaseId, request.Percent, request.ThumbnailsReady));
    }

    private static async Task<Results<NoContent, BadRequest<string>, NotFound, Conflict<string>>> Complete(
        Guid runId,
        WorkerCompleteRequest request,
        TranscodeQueue queue)
    {
        if (request.LeaseId == Guid.Empty)
        {
            return TypedResults.BadRequest("lease_id is required");
        }

        if (!double.IsFinite(request.LengthSeconds) || request.LengthSeconds is < 0 or > 86_400)
        {
            return TypedResults.BadRequest("length_seconds must be between 0 and 86400");
        }

        if (request.SizeBytes <= 0)
        {
            return TypedResults.BadRequest("size_bytes must be positive");
        }

        if (request.Outputs is { } outputs &&
            (outputs.ValueKind != JsonValueKind.Object || System.Text.Encoding.UTF8.GetByteCount(outputs.GetRawText()) > MaxOutputsBytes))
        {
            return TypedResults.BadRequest($"outputs must be an object of at most {MaxOutputsBytes} bytes");
        }

        return ToResult(await queue.CompleteAsync(runId, request.LeaseId, request.Outputs, request.LengthSeconds,
            request.SizeBytes));
    }

    private static async Task<Results<NoContent, BadRequest<string>, NotFound, Conflict<string>>> Fail(
        Guid runId,
        WorkerFailRequest request,
        TranscodeQueue queue)
    {
        if (request.LeaseId == Guid.Empty)
        {
            return TypedResults.BadRequest("lease_id is required");
        }

        if (string.IsNullOrWhiteSpace(request.Error))
        {
            return TypedResults.BadRequest("error is required");
        }

        return ToResult(await queue.FailAsync(runId, request.LeaseId, request.Error.Trim(), request.Retryable));
    }

    /// <summary>
    /// 409 tells the worker its lease is gone (the job was reclaimed or has ended) and it should drop the job.
    /// </summary>
    private static Results<NoContent, BadRequest<string>, NotFound, Conflict<string>> ToResult(TranscodeReportOutcome outcome) =>
        outcome switch
        {
            TranscodeReportOutcome.Applied => TypedResults.NoContent(),
            TranscodeReportOutcome.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Conflict("This job is no longer running under your lease")
        };
}

public static class WorkerSetup
{
    /// <summary>
    /// The transcode queue and the worker API's authentication. The worker endpoints stay mapped in the
    /// <c>OpenApi</c> environment but out of the document, and with no secret there they refuse everything; nothing
    /// here runs in the background, since workers pull jobs and each claim reclaims stale ones.
    /// </summary>
    public static void AddTranscodeWorkers(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TranscodingOptions>(builder.Configuration.GetSection(TranscodingOptions.SectionName));
        builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
        builder.Services.AddScoped<TranscodeStatements>();
        builder.Services.AddScoped<TranscodeQueue>();

        builder.Services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, WorkerAuthentication>(
                WorkerAuthentication.SchemeName, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(WorkerAuthentication.Policy, policy => policy
                .AddAuthenticationSchemes(WorkerAuthentication.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(WorkerAuthentication.WorkerClaim));
    }
}
