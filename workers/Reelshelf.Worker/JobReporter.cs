using Microsoft.Extensions.Logging;

namespace Reelshelf.Worker;

/// <summary>
/// Sends a running job's progress, which is also its heartbeat: when it has moved 5% or 10 seconds have passed since
/// the last report with a change, and at least every <see cref="WorkerSettings.HeartbeatInterval"/> regardless. A
/// 409 cancels <see cref="LeaseLost"/>, so the job stops and is dropped; other failures to report are only logged,
/// since the next report or the completion will carry on.
/// </summary>
public sealed class JobReporter : IAsyncDisposable
{
    private const int PercentStep = 5;
    private static readonly TimeSpan ChangeInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly WorkerApiClient api;
    private readonly WorkerJob job;
    private readonly ILogger logger;
    private readonly TimeSpan heartbeatInterval;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly CancellationTokenSource leaseLost = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task pump;

    private int disposed;
    private int encoding;
    private int percent;
    private bool thumbnailsReady;
    private int sentPercent = -1;
    private DateTimeOffset sentAt;

    public JobReporter(WorkerApiClient api, WorkerJob job, ILogger logger, TimeSpan heartbeatInterval, TimeProvider? time = null)
    {
        this.api = api;
        this.job = job;
        this.logger = logger;
        this.heartbeatInterval = heartbeatInterval;
        this.time = time ?? TimeProvider.System;
        sentAt = this.time.GetUtcNow();
        pump = Task.Run(PumpAsync);
    }

    /// <summary>Cancelled when the API says the job is no longer this worker's.</summary>
    public CancellationToken LeaseLost => leaseLost.Token;

    /// <summary>
    /// Whether the job's encode (or trim) has started. Until then a shutdown hands the job back at once, since
    /// nothing worth waiting for is under way; after, it gets the grace period to finish.
    /// </summary>
    public bool Encoding => Volatile.Read(ref encoding) == 1;

    /// <summary>Marks the start of the job's encode; see <see cref="Encoding"/>.</summary>
    public void EncodeStarting()
    {
        Volatile.Write(ref encoding, 1);
    }

    /// <summary>Records the encode's progress; the pump sends it when due.</summary>
    public void Report(int value)
    {
        Volatile.Write(ref percent, Math.Clamp(value, 0, 99));
    }

    /// <summary>
    /// Tells the API the six frames are uploaded, before any encode progress. Every later report repeats it, so one
    /// that doesn't get through is made up by the next. Throws <see cref="LeaseLostException"/> when the job is gone.
    /// </summary>
    public async Task ThumbnailsReadyAsync(CancellationToken cancellationToken)
    {
        thumbnailsReady = true;
        await SendAsync(cancellationToken);
    }

    private async Task PumpAsync()
    {
        using PeriodicTimer timer = new(Tick, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                int current = Volatile.Read(ref percent);
                TimeSpan since = time.GetUtcNow() - sentAt;
                bool moved = current >= sentPercent + PercentStep || (current != sentPercent && since >= ChangeInterval);
                if (moved || since >= heartbeatInterval)
                {
                    await SendAsync(stop.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private async Task SendAsync(CancellationToken cancellationToken)
    {
        await sending.WaitAsync(cancellationToken);
        try
        {
            int current = Volatile.Read(ref percent);
            await api.ReportProgressAsync(job, current, thumbnailsReady, cancellationToken);
            sentPercent = current;
            sentAt = time.GetUtcNow();
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Job {RunId} was taken away from this worker; dropping it", job.RunId);
            await leaseLost.CancelAsync();
            throw;
        }
        catch (WorkerApiException exception)
        {
            logger.LogWarning("Could not report progress for job {RunId}: {Error}", job.RunId, exception.Message);
            sentAt = time.GetUtcNow();
        }
        finally
        {
            sending.Release();
        }
    }

    /// <summary>Stops the pump. Safe to call more than once; <see cref="LeaseLost"/> stays readable afterwards.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await stop.CancelAsync();
        try
        {
            await pump;
        }
        catch (LeaseLostException)
        {
            // Already acted on through LeaseLost.
        }

        stop.Dispose();
    }
}
