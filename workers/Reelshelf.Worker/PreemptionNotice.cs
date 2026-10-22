using System.Net.Http.Headers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

/// <summary>
/// Tells the worker its VM is about to be taken away. On a Spot VM the job can't count on SIGTERM and the shutdown
/// grace: Batch on GCE stopped the container about 9 s into a preemption without either (the 2026-10-22 smoke test,
/// docs/video-pipeline/batch-overflow.md). So the worker watches for the notice itself and hands its job back at once.
/// </summary>
public interface IPreemptionNotice
{
    /// <summary>Cancelled when the VM has been preempted.</summary>
    CancellationToken Preempted { get; }
}

/// <summary>No notice: the dedicated worker and ECS tasks, which get SIGTERM.</summary>
public sealed class NoPreemptionNotice : IPreemptionNotice
{
    public CancellationToken Preempted => CancellationToken.None;
}

/// <summary>
/// Watches GCE's metadata server for <c>instance/preempted</c> turning <c>TRUE</c>, which happens at the start of a Spot
/// VM's 30 s notice, with a hanging GET (<c>wait_for_change</c>). Errors are logged and retried; off GCE it only logs,
/// so <c>Worker:PreemptionNotice=gce</c> belongs on GCE workers only.
/// </summary>
public sealed class GcePreemptionWatcher(
    IHttpClientFactory httpClientFactory,
    IOptions<WorkerSettings> options,
    ILogger<GcePreemptionWatcher> logger) : BackgroundService, IPreemptionNotice
{
    public const string HttpClientName = "gce-metadata";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource preempted = new();

    public CancellationToken Preempted => preempted.Token;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        string baseUrl = options.Value.GceMetadataUrl.TrimEnd('/') + "/instance/preempted";
        bool first = true;
        string etag = "";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The first read answers at once; later ones hang until the value differs from the last one read (its
                // ETag) or the server's timeout.
                string url = first
                    ? baseUrl
                    : $"{baseUrl}?wait_for_change=true&timeout_sec=300" +
                      (etag.Length > 0 ? $"&last_etag={Uri.EscapeDataString(etag)}" : "");
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                request.Headers.Add("Metadata-Flavor", "Google");
                using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
                response.EnsureSuccessStatusCode();
                string value = (await response.Content.ReadAsStringAsync(stoppingToken)).Trim();
                if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("This Spot VM has been preempted");
                    await preempted.CancelAsync();
                    return;
                }

                first = false;
                // GCE sends the ETag unquoted, which the typed header refuses.
                etag = response.Headers.NonValidated.TryGetValues("ETag", out HeaderStringValues tags)
                    ? tags.ToString().Trim('"')
                    : "";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Could not read the preemption notice from the metadata server, retrying in {Delay}: {Error}",
                    RetryDelay, exception.Message);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public override void Dispose()
    {
        preempted.Dispose();
        base.Dispose();
    }
}
