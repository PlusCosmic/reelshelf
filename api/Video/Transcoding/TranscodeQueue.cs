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

/// <summary>
/// A job handed to a worker, with the presigned URLs it reads its input from and writes its outputs to (see
/// <see cref="ITranscodeStorage.UrlsFor"/>).
/// </summary>
public sealed record ClaimedTranscodeRun(
    Guid RunId,
    Guid LeaseId,
    Guid ClipId,
    string Type,
    int VideoVersion,
    int Attempt,
    JsonElement Input,
    string? SourceUrl = null,
    IReadOnlyDictionary<string, string>? UploadUrls = null);

/// <summary>
/// The transcode job queue (<c>transcode_run</c>). The video host enqueues jobs; workers claim them and report
/// back through <see cref="WorkerEndpoints"/>, never touching Postgres themselves, and results reach the clip
/// through <see cref="ClipVideoUpdateHandler"/>. A <c>transcode</c> drives the clip's status; a finished
/// <c>transcode</c> or <c>trim</c> moves the clip to its new video version, after which the storage deletes the
/// uploaded source and any version it replaced. A finished <c>migrate</c> moves its Bunny clip to R2; until then it
/// never touches the clip, which keeps playing from Bunny (docs/video-pipeline/bunny-migration.md). The other types
/// record their output on the run until the features that use them land.
/// </summary>
public class TranscodeQueue(
    TranscodeStatements statements,
    ClipVideoUpdateHandler updateHandler,
    ITranscodeStorage storage,
    [FromKeyedServices(VideoHostNames.Bunny)] IClipVideoMediaUrls bunnyMediaUrls,
    IOptionsMonitor<TranscodingOptions> options,
    ILogger<TranscodeQueue> logger)
{
    /// <summary>The version a <c>migrate</c> job writes, and the one its clip plays from on R2 once it moves.</summary>
    public const int MigratedVideoVersion = 1;

    /// <summary>
    /// How many migrate jobs one claim fails for clips no longer on Bunny before it gives up for this call, so a
    /// backlog of them can't hold a worker's request for long.
    /// </summary>
    private const int MaxSkippedMigrations = 10;

    /// <summary>The longest error a worker's failure keeps.</summary>
    public const int MaxErrorLength = 4000;

    /// <summary>
    /// How long a claimed job's presigned URLs live: past <see cref="TranscodingOptions.StaleAfter"/>, after which a
    /// quiet job is reclaimed with fresh ones, with an hour to spare for a worker still uploading at that point.
    /// </summary>
    public static TimeSpan PresignLifetime(TranscodingOptions options) =>
        (options.StaleAfter < TimeSpan.Zero ? TimeSpan.Zero : options.StaleAfter) + TimeSpan.FromHours(1);

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
    /// first, so a clip whose transcode keeps dying ends <c>encode_failed</c> rather than waiting for ever. A
    /// <c>migrate</c> job is handed out only when there is storage to write it to, and only while its clip is still on
    /// Bunny: its <c>source_url</c> is the clip's Bunny HLS playlist, and one whose clip has moved is failed instead.
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

        for (int skipped = 0; skipped <= MaxSkippedMigrations; skipped++)
        {
            TranscodeStatements.ClaimedRunRow? claimed = await statements.ClaimNextRunAsync(maxAttempts,
                current.StaleAfter, includeMigrate: storage.IsConfigured);
            if (claimed is null)
            {
                return null;
            }

            if (claimed.Type == TranscodeRunTypes.Migrate && claimed.CurrentVideoHost != VideoHostNames.Bunny)
            {
                await statements.FailRunAsync(claimed.Id, claimed.LeaseId, "The clip is no longer on Bunny",
                    retryable: false, maxAttempts, current.RetryAfter);
                logger.LogWarning("Failed migrate job {RunId}: clip {ClipId} is on {VideoHost}, not Bunny",
                    claimed.Id, claimed.ClipId, claimed.CurrentVideoHost);
                continue;
            }

            logger.LogInformation("Worker claimed {Type} job {RunId} for clip {ClipId} (attempt {Attempt})",
                claimed.Type, claimed.Id, claimed.ClipId, claimed.Attempts);
            TranscodeJobUrls urls = storage.UrlsFor(claimed.ClipId, claimed.Type, claimed.VideoVersion,
                claimed.CurrentVideoVersion, PresignLifetime(current));
            string? sourceUrl = claimed.Type == TranscodeRunTypes.Migrate
                ? bunnyMediaUrls.ServerStream(new ClipVideoRef(VideoHostNames.Bunny, claimed.CurrentVideoId)).ToString()
                : urls.SourceUrl;
            using JsonDocument input = JsonDocument.Parse(claimed.Input);
            return new ClaimedTranscodeRun(claimed.Id, claimed.LeaseId, claimed.ClipId, claimed.Type,
                claimed.VideoVersion, claimed.Attempts, input.RootElement.Clone(), sourceUrl, urls.UploadUrls);
        }

        return null;
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
    /// Records a finished job. A transcode's or trim's clip becomes ready on the job's video version, with the
    /// output's length, size, codecs and height (<see cref="TranscodeOutputs"/>), in the same transaction. After it
    /// commits, a transcode's uploaded source is deleted, as is any version the clip moved off, and a transcode's
    /// clip gets its legend detection and transcription queued. A repeat of the report (the worker retrying after a
    /// lost response) is accepted and changes nothing, beyond retrying the source delete and re-queueing the
    /// follow-ups, which are idempotent. A migrate's clip moves from Bunny to R2 on version 1 in the same transaction,
    /// with nothing queued after; if the clip is no longer on Bunny by then, the run still succeeds, the clip is left
    /// alone and the job's uploads are deleted (unless the clip plays from R2 version 1 itself).
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
        TranscodeOutputs reported = TranscodeOutputs.From(outputs);

        int? replacedVersion = null;
        bool? migrated = null;
        TranscodeStatements.FinishedRunRow? run = await statements.InTransactionAsync(async () =>
        {
            TranscodeStatements.FinishedRunRow? completed = await statements.CompleteRunAsync(runId, leaseId, outputJson);
            if (completed is { Type: TranscodeRunTypes.Transcode or TranscodeRunTypes.Trim })
            {
                int? previousVersion = await updateHandler.ApplyTranscodeCompletedAsync(completed.ClipId,
                    completed.VideoVersion, clipLength, sizeBytes, reported.Codecs, reported.Height);
                replacedVersion = previousVersion is { } previous && previous != completed.VideoVersion ? previous : null;
            }
            else if (completed is { Type: TranscodeRunTypes.Migrate })
            {
                migrated = await updateHandler.ApplyMigrationCompletedAsync(completed.ClipId, clipLength, sizeBytes,
                    reported.Codecs, reported.Height);
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
                Id = state.Id, ClipId = state.ClipId, Type = state.Type, Status = state.Status,
                VideoVersion = state.VideoVersion
            };
        }
        else
        {
            logger.LogInformation("{Type} job {RunId} for clip {ClipId} succeeded ({Length}s, {Size} bytes)",
                run.Type, run.Id, run.ClipId, lengthSeconds, sizeBytes);
        }

        if (run.Type == TranscodeRunTypes.Migrate)
        {
            // A repeated report (migrated is null) can't tell whether the first one moved the clip, so it deletes
            // nothing.
            if (migrated == true)
            {
                logger.LogInformation("Moved clip {ClipId} from Bunny to R2", run.ClipId);
            }
            else if (migrated == false)
            {
                await DeleteAbandonedMigrationAsync(run);
            }

            return TranscodeReportOutcome.Applied;
        }

        await CleanUpAfterCompletionAsync(run, replacedVersion);
        if (run.Type == TranscodeRunTypes.Transcode)
        {
            await updateHandler.QueueEncodedFollowUpsAsync(run.ClipId);
        }

        return TranscodeReportOutcome.Applied;
    }

    /// <summary>
    /// Deletes what a finished job leaves behind: a transcode's source upload, and the version a transcode or trim
    /// replaced. The clip already plays from its new version, so a failure here is logged rather than failing the
    /// worker's report; the objects then stay until the clip is deleted.
    /// </summary>
    private async Task CleanUpAfterCompletionAsync(TranscodeStatements.FinishedRunRow run, int? replacedVersion)
    {
        try
        {
            if (run.Type == TranscodeRunTypes.Transcode)
            {
                await storage.DeleteSourceAsync(run.ClipId);
            }

            if (replacedVersion is { } version)
            {
                await storage.DeleteVersionAsync(run.ClipId, version);
                logger.LogInformation("Deleted version {Version} of clip {ClipId}, replaced by version {NewVersion}",
                    version, run.ClipId, run.VideoVersion);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not clean up after {Type} job {RunId} for clip {ClipId}", run.Type, run.Id,
                run.ClipId);
        }
    }

    /// <summary>
    /// Deletes the outputs of a migrate job whose clip left Bunny before it completed, so nothing plays from them. A
    /// clip that is on R2 version 1 by then plays from those very keys, so they stay. Failures are logged, as in
    /// <see cref="CleanUpAfterCompletionAsync"/>.
    /// </summary>
    private async Task DeleteAbandonedMigrationAsync(TranscodeStatements.FinishedRunRow run)
    {
        try
        {
            TranscodeStatements.ClipVersionRow? clip = await statements.GetClipVersionAsync(run.ClipId);
            if (clip is { VideoHost: VideoHostNames.R2, VideoVersion: MigratedVideoVersion })
            {
                logger.LogWarning(
                    "Migrate job {RunId} finished after clip {ClipId} was already on R2 version {Version}; kept its files",
                    run.Id, run.ClipId, MigratedVideoVersion);
                return;
            }

            await storage.DeleteVersionAsync(run.ClipId, MigratedVideoVersion);
            logger.LogInformation(
                "Migrate job {RunId} finished after clip {ClipId} left Bunny; deleted its version {Version} outputs",
                run.Id, run.ClipId, MigratedVideoVersion);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the outputs of migrate job {RunId} for clip {ClipId}", run.Id,
                run.ClipId);
        }
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

    /// <summary>
    /// Takes a job back from a worker that is shutting down (a deploy or restart), not one that failed: it is
    /// pending again and claimable at once, and the attempt its claim counted is refunded, up to
    /// <see cref="TranscodingOptions.MaxReleases"/> releases per job. A transcode's processing clip goes back to
    /// <c>uploaded</c> in the same transaction. A repeat of the report under the same lease is accepted and changes
    /// nothing.
    /// </summary>
    public async Task<TranscodeReportOutcome> ReleaseAsync(Guid runId, Guid leaseId)
    {
        int maxReleases = options.CurrentValue.EffectiveMaxReleases;
        TranscodeStatements.ReleasedRunRow? run = await statements.InTransactionAsync(async () =>
        {
            TranscodeStatements.ReleasedRunRow? released = await statements.ReleaseRunAsync(runId, leaseId, maxReleases);
            if (released is { Type: TranscodeRunTypes.Transcode })
            {
                await updateHandler.ApplyTranscodeReleasedAsync(released.ClipId);
            }

            return released;
        });

        if (run is null)
        {
            TranscodeStatements.RunStateRow? state = await statements.GetRunAsync(runId);
            if (state is null)
            {
                return TranscodeReportOutcome.NotFound;
            }

            // The same worker repeating its release finds it recorded under its lease.
            return state.ReleasedLeaseId == leaseId ? TranscodeReportOutcome.Applied : TranscodeReportOutcome.LeaseLost;
        }

        if (run.Refunded)
        {
            logger.LogInformation(
                "{Type} job {RunId} for clip {ClipId} was handed back by a worker shutting down (release {Releases}); attempt refunded",
                run.Type, run.Id, run.ClipId, run.ReleaseCount);
        }
        else
        {
            logger.LogWarning(
                "{Type} job {RunId} for clip {ClipId} was handed back by a worker shutting down {Releases} times, more than {MaxReleases}; the attempt counts ({Attempts} used)",
                run.Type, run.Id, run.ClipId, run.ReleaseCount, maxReleases, run.Attempts);
        }

        return TranscodeReportOutcome.Applied;
    }
}
