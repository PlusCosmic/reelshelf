using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

/// <summary>
/// Claims jobs one at a time and runs each to a report: complete, fail, or nothing when the lease was lost (another
/// worker has the job now). Waits longer while the queue is empty or the API is failing. On SIGTERM a job whose encode
/// is under way gets <see cref="WorkerSettings.ShutdownGrace"/> to finish; past that it is stopped and released, which
/// puts it back in the queue without using an attempt. A job that hasn't started encoding is stopped and released at
/// once. Releasing is only for shutdown: a job that fails, ffmpeg crashing included, is reported as failed. With
/// <see cref="WorkerSettings.ExitWhenIdleAfter"/> set, the worker stops the host (exit code 0) once it has gone that
/// long without claiming a job. When <see cref="IPreemptionNotice"/> says the Spot VM is being preempted, the job is
/// stopped and released at once, whatever stage it is at, and the worker claims nothing more; it then waits for the
/// VM to go, so Batch sees the preemption (and retries the task) rather than a worker that exited.
/// </summary>
public sealed class WorkerLoop(
    WorkerApiClient api,
    JobRunner runner,
    FfmpegRunner ffmpeg,
    IOptions<WorkerSettings> options,
    ILogger<WorkerLoop> logger,
    IHostApplicationLifetime? lifetime = null,
    TimeProvider? timeProvider = null,
    IPreemptionNotice? preemptionNotice = null) : BackgroundService
{
    /// <summary>How long a report may take once the worker is shutting down.</summary>
    private static readonly TimeSpan ShutdownReportBudget = TimeSpan.FromSeconds(10);

    private readonly WorkerSettings settings = options.Value;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly CancellationToken preempted = preemptionNotice?.Preempted ?? CancellationToken.None;
    private FfmpegCapabilities? capabilities;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartAsync();
        using CancellationTokenSource polling = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, preempted);
        if (await PollAsync(RunOnceAsync, polling.Token))
        {
            // Exiting when idle is the expected end of an overflow worker, not a failure.
            Environment.ExitCode = 0;
            lifetime?.StopApplication();
        }

        if (preempted.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Exiting now would look like a finished task to Batch, which would then not run it again elsewhere.
            logger.LogInformation("Preempted: claiming nothing more, waiting for the VM to stop");
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        }

        logger.LogInformation("Worker stopped");
    }

    /// <summary>
    /// Runs <paramref name="runOnce"/> (a claim, and the job when there was one) until shutdown, waiting between empty
    /// claims and after API errors. True when it stopped because it had gone <see cref="WorkerSettings.ExitWhenIdleAfter"/>
    /// without claiming a job; false on shutdown.
    /// </summary>
    internal async Task<bool> PollAsync(Func<CancellationToken, Task<bool>> runOnce, CancellationToken stoppingToken)
    {
        Backoff idle = new(settings.IdlePollInterval, settings.MaxIdlePollInterval);
        Backoff errors = new(settings.ErrorBackoff, settings.MaxErrorBackoff);
        // Time spent on empty claims and the waits after them since the last job; the waits after API errors don't
        // count, so an outage of the API doesn't make every overflow worker exit.
        TimeSpan idleFor = TimeSpan.Zero;
        TimeSpan? exitAfter = settings.ExitWhenIdleAfter;
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset started = time.GetUtcNow();
            TimeSpan delay;
            bool countsAsIdle;
            try
            {
                if (await runOnce(stoppingToken))
                {
                    idle.Reset();
                    errors.Reset();
                    idleFor = TimeSpan.Zero;
                    continue;
                }

                errors.Reset();
                idleFor += time.GetUtcNow() - started;
                if (idleFor >= exitAfter)
                {
                    logger.LogInformation("No job claimed for {Idle}; exiting (Worker:ExitWhenIdleAfter is {Limit})",
                        idleFor, exitAfter);
                    return true;
                }

                delay = idle.Next();
                if (exitAfter - idleFor is { } remaining && remaining < delay)
                {
                    // Claim once more right at the limit rather than sleeping past it.
                    delay = remaining;
                }

                countsAsIdle = true;
            }
            catch (Exception exception)
            {
                // A claim the API refused or answered with something unreadable; jobs report their own failures.
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                delay = errors.Next();
                countsAsIdle = false;
                logger.LogWarning("Could not claim a job, retrying in {Delay}: {Error}", delay, exception.Message);
            }

            DateTimeOffset waiting = time.GetUtcNow();
            try
            {
                await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (countsAsIdle)
            {
                idleFor += time.GetUtcNow() - waiting;
            }
        }

        return false;
    }

    /// <summary>Checks ffmpeg and clears work left by a worker that died mid-job.</summary>
    internal async Task StartAsync()
    {
        capabilities = await ffmpeg.CapabilitiesAsync(CancellationToken.None);
        if (!capabilities.SvtAv1 || !capabilities.Opus)
        {
            Environment.ExitCode = 1;
            throw new InvalidOperationException(
                $"ffmpeg ({settings.FfmpegPath}) lacks libsvtav1 or libopus; the worker can't encode with it");
        }

        if (!capabilities.CanToneMap)
        {
            logger.LogWarning("ffmpeg has no zscale/tonemap filter: HDR sources will be refused");
        }

        if (Directory.Exists(settings.WorkDirectory))
        {
            foreach (string stale in Directory.EnumerateDirectories(settings.WorkDirectory))
            {
                TryDelete(stale);
            }
        }

        Directory.CreateDirectory(settings.WorkDirectory);
        logger.LogInformation(
            "Worker started against {Api}: SVT-AV1 preset {Preset}, lp {Lp}, {Bitrate} kbps ({HighFrameRateBitrate} kbps above 32 fps), nice {Nice}",
            settings.ApiBaseUrl, settings.Preset, settings.LogicalProcessors, settings.TargetBitrateKbps,
            settings.HighFrameRateBitrateKbps, settings.Niceness);
    }

    /// <summary>Claims and runs one job. False when the queue had none. Throws <see cref="WorkerApiException"/> when the claim fails.</summary>
    internal async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        if (preempted.IsCancellationRequested)
        {
            return false;
        }

        // The claim isn't cancelled by shutdown: a job the API has already handed over is better run (or handed
        // back) than left to go stale. HttpClient's timeout bounds it.
        WorkerJob? job = await api.ClaimAsync(CancellationToken.None);
        if (job is null)
        {
            return false;
        }

        await RunJobAsync(job, stoppingToken);
        return true;
    }

    private async Task RunJobAsync(WorkerJob job, CancellationToken stoppingToken)
    {
        logger.LogInformation("Claimed {Type} job {RunId} for clip {ClipId} (version {Version}, attempt {Attempt})",
            job.Type, job.RunId, job.ClipId, job.VideoVersion, job.Attempt);

        string workDirectory = Path.Combine(settings.WorkDirectory, job.RunId.ToString("N"));
        using CancellationTokenSource shutdown = new();
        using CancellationTokenSource reportBudget = new();
        JobReporter reporter = new(api, job, logger, settings.HeartbeatInterval);
        await using CancellationTokenRegistration onPreempted = preempted.Register(() =>
        {
            // The VM has seconds left: no grace, whatever the stage.
            logger.LogWarning("Preempted: job {RunId} goes back now", job.RunId);
            reportBudget.CancelAfter(ShutdownReportBudget);
            shutdown.Cancel();
        });
        await using CancellationTokenRegistration onStop = stoppingToken.Register(() =>
        {
            if (preempted.IsCancellationRequested)
            {
                return;
            }

            if (reporter.Encoding)
            {
                logger.LogInformation("Shutting down: job {RunId} has {Grace} to finish", job.RunId, settings.ShutdownGrace);
                shutdown.CancelAfter(settings.ShutdownGrace);
                reportBudget.CancelAfter(settings.ShutdownGrace + ShutdownReportBudget);
            }
            else
            {
                logger.LogInformation("Shutting down: job {RunId} hasn't started encoding, so it goes back now", job.RunId);
                reportBudget.CancelAfter(ShutdownReportBudget);
                shutdown.Cancel();
            }
        });

        try
        {
            using CancellationTokenSource jobToken =
                CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, reporter.LeaseLost);
            TryDelete(workDirectory);
            Directory.CreateDirectory(workDirectory);

            JobResult result = await runner.RunAsync(job, workDirectory, capabilities!, reporter, jobToken.Token);
            await reporter.DisposeAsync();
            await api.CompleteAsync(job, result, reportBudget.Token);
            logger.LogInformation("Completed job {RunId}", job.RunId);
        }
        catch (Exception exception) when (exception is LeaseLostException ||
                                          (exception is OperationCanceledException && reporter.LeaseLost.IsCancellationRequested))
        {
            logger.LogWarning("Dropped job {RunId}: its lease is gone", job.RunId);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Stopped by the shutdown itself, so the job didn't fail: hand it back without using an attempt.
            await ReleaseAsync(job, reportBudget.Token);
        }
        catch (JobFailedException exception)
        {
            await FailAsync(job, exception.Message, exception.Retryable, reportBudget.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Job {RunId} failed", job.RunId);
            await FailAsync(job, exception.Message, retryable: true, reportBudget.Token);
        }
        finally
        {
            await reporter.DisposeAsync();
            TryDelete(workDirectory);
        }
    }

    private async Task FailAsync(WorkerJob job, string error, bool retryable, CancellationToken cancellationToken)
    {
        logger.LogWarning("Job {RunId} failed ({Retry}): {Error}", job.RunId, retryable ? "retryable" : "final", error);
        try
        {
            await api.FailAsync(job, error, retryable, cancellationToken);
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Job {RunId} was already taken away from this worker", job.RunId);
        }
        catch (Exception exception)
        {
            // The job stays running until the API's stale-after reclaims it.
            logger.LogError("Could not report job {RunId}'s failure: {Error}", job.RunId, exception.Message);
        }
    }

    private async Task ReleaseAsync(WorkerJob job, CancellationToken cancellationToken)
    {
        logger.LogWarning("Job {RunId} stopped by the shutdown; handing it back", job.RunId);
        try
        {
            await api.ReleaseAsync(job, cancellationToken);
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Job {RunId} was already taken away from this worker", job.RunId);
        }
        catch (Exception exception)
        {
            // The job stays running until the API's stale-after reclaims it, which uses an attempt.
            logger.LogError("Could not hand job {RunId} back: {Error}", job.RunId, exception.Message);
        }
    }

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not delete {Directory}: {Error}", directory, exception.Message);
        }
    }
}
