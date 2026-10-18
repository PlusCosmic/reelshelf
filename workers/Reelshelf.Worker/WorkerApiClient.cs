using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Reelshelf.Worker;

/// <summary>
/// The API's worker endpoints (<c>api/Video/Transcoding/WorkerEndpoints.cs</c>), in snake_case JSON with the worker
/// secret as a bearer token. A 409 (or 404) on a report means the job is no longer this worker's and throws
/// <see cref="LeaseLostException"/>. Completions and failures are retried on transient errors, which the API makes
/// safe: repeating one under the same lease changes nothing.
/// </summary>
public sealed class WorkerApiClient(IHttpClientFactory httpClientFactory, ILogger<WorkerApiClient> logger)
{
    public const string HttpClientName = "worker-api";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const int FinalReportAttempts = 6;

    /// <summary>The delay before retry <paramref name="attempt"/> (1-based); tests shorten it.</summary>
    internal Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(2 << (attempt - 1));

    /// <summary>The next job, or null when the queue has nothing for this worker.</summary>
    public async Task<WorkerJob?> ClaimAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync("api/worker/claim", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new WorkerApiException($"Claim failed with {(int)response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<WorkerJob>(JsonOptions, cancellationToken)
               ?? throw new WorkerApiException("Claim returned an empty job");
    }

    public async Task ReportProgressAsync(WorkerJob job, int percent, bool thumbnailsReady, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync($"api/worker/{job.RunId}/progress",
            new { lease_id = job.LeaseId, percent, thumbnails_ready = thumbnailsReady }, cancellationToken);
        EnsureReported(job, response, "Progress");
    }

    public Task CompleteAsync(WorkerJob job, JobResult result, CancellationToken cancellationToken) =>
        FinalReportAsync(job, "complete", new
        {
            lease_id = job.LeaseId,
            outputs = result.Outputs,
            length_seconds = result.LengthSeconds,
            size_bytes = result.SizeBytes
        }, cancellationToken);

    public Task FailAsync(WorkerJob job, string error, bool retryable, CancellationToken cancellationToken) =>
        FinalReportAsync(job, "fail", new { lease_id = job.LeaseId, error, retryable }, cancellationToken);

    private async Task FinalReportAsync(WorkerJob job, string report, object body, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using HttpResponseMessage response =
                    await SendAsync($"api/worker/{job.RunId}/{report}", body, cancellationToken);
                EnsureReported(job, response, report);
                return;
            }
            catch (WorkerApiException exception) when (attempt < FinalReportAttempts &&
                                                       !cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay = RetryDelay(attempt);
                logger.LogWarning("Could not report {Report} for job {RunId} (attempt {Attempt}), retrying in {Delay}: {Error}",
                    report, job.RunId, attempt, delay, exception.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object? body, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            return body is null
                ? await client.PostAsync(path, null, cancellationToken)
                : await client.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new WorkerApiException($"The API could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WorkerApiException("The API did not answer in time", exception);
        }
    }

    private static void EnsureReported(WorkerJob job, HttpResponseMessage response, string report)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent or HttpStatusCode.OK:
                return;
            case HttpStatusCode.Conflict or HttpStatusCode.NotFound:
                throw new LeaseLostException(job.RunId);
            case HttpStatusCode.BadRequest:
                // Not transient; retrying the same body won't help.
                throw new InvalidOperationException($"The API refused the {report} report for job {job.RunId} (400)");
            default:
                throw new WorkerApiException($"{report} for job {job.RunId} failed with {(int)response.StatusCode}");
        }
    }
}
