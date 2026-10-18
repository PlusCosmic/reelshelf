using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding;

/// <summary>How the queue took a worker's report.</summary>
public enum TranscodeReportOutcome
{
    /// <summary>Recorded, or already recorded by an earlier copy of the same report.</summary>
    Applied,

    /// <summary>There is no such job.</summary>
    NotFound,

    /// <summary>The job is no longer running under the worker's lease: it was reclaimed, or it has finished.</summary>
    LeaseLost
}

/// <summary>A job handed to a worker.</summary>
public sealed record ClaimedTranscodeRun(
    Guid RunId,
    Guid LeaseId,
    Guid ClipId,
    string Type,
    int VideoVersion,
    int Attempt,
    JsonElement Input);

/// <summary>
/// The transcode job queue (<c>transcode_run</c>). The video host enqueues jobs; workers claim them and report
/// back through <see cref="WorkerEndpoints"/>, never touching Postgres themselves, and results reach the clip
/// through <see cref="ClipVideoUpdateHandler"/>. Only <c>transcode</c> jobs change a clip's status today; the other
/// types record their output on the run until the features that use them land.
/// </summary>
public class TranscodeQueue(
    TranscodeStatements statements,
    ClipVideoUpdateHandler updateHandler,
    IOptionsMonitor<TranscodingOptions> options,
    ILogger<TranscodeQueue> logger)
{
    /// <summary>The longest error a worker's failure keeps.</summary>
    public const int MaxErrorLength = 4000;

    /// <summary>
    /// Queues a job for a clip, as the video host does when an upload completes. Returns its id, or null when the
    /// job is a <c>transcode</c> and the clip already has one pending or running (one active transcode per clip).
    /// </summary>
    public async Task<Guid?> EnqueueAsync(
        Guid clipId,
        string type,
        TranscodeRunInput input,
        int videoVersion = 1,
        int? priority = null)
    {
        if (!TranscodeRunTypes.All.Contains(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown transcode job type");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(videoVersion, 1);

        Guid? runId = await statements.EnqueueAsync(clipId, type, input.ToJson(), videoVersion,
            priority ?? TranscodeRunTypes.DefaultPriority(type));
        if (runId is { } id)
        {
            logger.LogInformation("Queued {Type} job {RunId} for clip {ClipId}", type, id, clipId);
        }

        return runId;
    }

    /// <summary>
    /// Hands the next job to a worker, or null when there is none. Jobs that ran out of attempts are failed
    /// first, so a clip whose transcode keeps dying ends <c>encode_failed</c> rather than waiting for ever.
    /// </summary>
    public async Task<ClaimedTranscodeRun?> ClaimAsync()
    {
        TranscodingOptions current = options.CurrentValue;
        int maxAttempts = current.EffectiveMaxAttempts;

        List<TranscodeStatements.FinishedRunRow> exhausted = await statements.InTransactionAsync(async () =>
        {
            List<TranscodeStatements.FinishedRunRow> runs =
                await statements.FailExhaustedRunsAsync(maxAttempts, current.StaleAfter);
            foreach (TranscodeStatements.FinishedRunRow run in runs.Where(run => run.Type == TranscodeRunTypes.Transcode))
            {
                await updateHandler.ApplyTranscodeFailedAsync(run.ClipId);
            }

            return runs;
        });
        foreach (TranscodeStatements.FinishedRunRow run in exhausted)
        {
            logger.LogWarning("Gave up on {Type} job {RunId} for clip {ClipId} after {MaxAttempts} attempts",
                run.Type, run.Id, run.ClipId, maxAttempts);
        }

        TranscodeStatements.ClaimedRunRow? claimed = await statements.ClaimNextRunAsync(maxAttempts, current.StaleAfter);
        if (claimed is null)
        {
            return null;
        }

        logger.LogInformation("Worker claimed {Type} job {RunId} for clip {ClipId} (attempt {Attempt})",
            claimed.Type, claimed.Id, claimed.ClipId, claimed.Attempts);
        using JsonDocument input = JsonDocument.Parse(claimed.Input);
        return new ClaimedTranscodeRun(claimed.Id, claimed.LeaseId, claimed.ClipId, claimed.Type,
            claimed.VideoVersion, claimed.Attempts, input.RootElement.Clone());
    }

    /// <summary>
    /// Records a worker's progress, which also tells the queue the worker is alive. A transcode's clip shows as
    /// processing at that percentage; the first report with <paramref name="thumbnailsReady"/> can queue legend
    /// detection early.
    /// </summary>
    public async Task<TranscodeReportOutcome> ReportProgressAsync(Guid runId, Guid leaseId, int percent, bool thumbnailsReady)
    {
        TranscodeStatements.ProgressRunRow? run = await statements.InTransactionAsync(async () =>
        {
            TranscodeStatements.ProgressRunRow? updated =
                await statements.ReportProgressAsync(runId, leaseId, percent, thumbnailsReady);
            if (updated is { Type: TranscodeRunTypes.Transcode })
            {
                await updateHandler.ApplyTranscodeProgressAsync(updated.ClipId, updated.Progress);
            }

            return updated;
        });
        if (run is null)
        {
            // Progress is not retried after the job ends, so there is nothing to be idempotent about.
            return await statements.GetRunAsync(runId) is null
                ? TranscodeReportOutcome.NotFound
                : TranscodeReportOutcome.LeaseLost;
        }

        if (run is { Type: TranscodeRunTypes.Transcode, ThumbnailsNewlyReady: true } &&
            options.CurrentValue.QueueDetectionOnThumbnails)
        {
            await updateHandler.QueueDetectionOnThumbnailsAsync(run.ClipId);
        }

        return TranscodeReportOutcome.Applied;
    }

    /// <summary>
    /// Records a finished job. A transcode's clip becomes ready with the output's length and size, in the same
    /// transaction, and then gets its legend detection and transcription queued. A repeat of the report (the
    /// worker retrying after a lost response) is accepted and changes nothing, beyond re-queueing the follow-ups,
    /// which are idempotent.
    /// </summary>
    public async Task<TranscodeReportOutcome> CompleteAsync(
        Guid runId,
        Guid leaseId,
        JsonElement? outputs,
        double lengthSeconds,
        long sizeBytes)
    {
        string outputJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outputs"] = outputs,
            ["length_seconds"] = lengthSeconds,
            ["size_bytes"] = sizeBytes
        });
        int clipLength = (int)Math.Round(lengthSeconds, MidpointRounding.AwayFromZero);

        TranscodeStatements.FinishedRunRow? run = await statements.InTransactionAsync(async () =>
        {
            TranscodeStatements.FinishedRunRow? completed = await statements.CompleteRunAsync(runId, leaseId, outputJson);
            if (completed is { Type: TranscodeRunTypes.Transcode })
            {
                await updateHandler.ApplyTranscodeCompletedAsync(completed.ClipId, clipLength, sizeBytes);
            }

            return completed;
        });

        if (run is null)
        {
            TranscodeStatements.RunStateRow? state = await statements.GetRunAsync(runId);
            if (state is null)
            {
                return TranscodeReportOutcome.NotFound;
            }

            if (state.Status != "succeeded" || state.LeaseId != leaseId)
            {
                return TranscodeReportOutcome.LeaseLost;
            }

            run = new TranscodeStatements.FinishedRunRow
            {
                Id = state.Id, ClipId = state.ClipId, Type = state.Type, Status = state.Status
            };
        }
        else
        {
            logger.LogInformation("{Type} job {RunId} for clip {ClipId} succeeded ({Length}s, {Size} bytes)",
                run.Type, run.Id, run.ClipId, lengthSeconds, sizeBytes);
        }

        if (run.Type == TranscodeRunTypes.Transcode)
        {
            await updateHandler.QueueEncodedFollowUpsAsync(run.ClipId);
        }

        return TranscodeReportOutcome.Applied;
    }

    /// <summary>
    /// Records a failed attempt. A retryable failure with attempts left goes back in the queue after the retry
    /// delay; otherwise the job fails, and a failed transcode sets its clip to <c>encode_failed</c>. A repeat of
    /// the report is accepted and changes nothing.
    /// </summary>
    public async Task<TranscodeReportOutcome> FailAsync(Guid runId, Guid leaseId, string error, bool retryable)
    {
        TranscodingOptions current = options.CurrentValue;
        string trimmed = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        TranscodeStatements.FinishedRunRow? run = await statements.InTransactionAsync(async () =>
        {
            TranscodeStatements.FinishedRunRow? failed = await statements.FailRunAsync(
                runId, leaseId, trimmed, retryable, current.EffectiveMaxAttempts, current.RetryAfter);
            if (failed is { Type: TranscodeRunTypes.Transcode, Status: "failed" })
            {
                await updateHandler.ApplyTranscodeFailedAsync(failed.ClipId);
            }

            return failed;
        });

        if (run is null)
        {
            TranscodeStatements.RunStateRow? state = await statements.GetRunAsync(runId);
            if (state is null)
            {
                return TranscodeReportOutcome.NotFound;
            }

            // The same worker repeating its report finds the job pending (retrying) or failed under its lease.
            return state.LeaseId == leaseId && state.Status is "pending" or "failed"
                ? TranscodeReportOutcome.Applied
                : TranscodeReportOutcome.LeaseLost;
        }

        logger.LogWarning("{Type} job {RunId} for clip {ClipId} failed ({Outcome}): {Error}",
            run.Type, run.Id, run.ClipId, run.Status == "pending" ? "will retry" : "giving up", trimmed);
        return TranscodeReportOutcome.Applied;
    }
}
