using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.Plans;
using Reelshelf.Video;
using Reelshelf.Video.R2;

namespace Reelshelf.Moderation;

/// <summary>
/// Keeps a clip removed after a child-safety or terrorism report, privately, and takes it off public access. The
/// removal itself already deleted the clip row, so nothing in Reelshelf lists or plays it. The law requires the
/// content to be kept (reelshelf-docs/moderation.md, "Legal basis"), so nothing of it is deleted until a copy is in the private
/// evidence bucket:
/// <list type="number">
/// <item><b>Quarantine</b> (done once <c>cdn_purge</c> is set): every object of the clip in the media bucket is copied
/// server side to <c>quarantine/{evidenceId}/</c> and the original deleted, then its URLs are purged from the media
/// domain. This needs only the media bucket, so it happens even while the evidence bucket fails.</item>
/// <item><b>Preserve</b> (<c>files</c>): <c>record.json</c> and the video go to the evidence bucket, from the
/// quarantine.</item>
/// <item><b>Delete</b> (<c>media_deleted_at</c>): only then is the video deleted at its host, with the quarantine; the
/// record is <c>preserved</c>.</item>
/// <item><b>Sweep</b> (<c>swept_at</c>), after <c>SweepAfter</c>: anything a transcode worker uploaded into
/// <c>clips/{clipId}/</c> since is quarantined again (deleted, once preserved) and purged.</item>
/// </list>
/// Each step is recorded as it finishes and is safe to repeat, so a failed attempt resumes where it stopped, and is
/// written only under the claim that did it, so a processor whose claim went stale writes nothing. Preserving is never
/// given up on: after <c>EvidenceMaxAttempts</c> the record is <c>failed</c> (needs attention, logged at Error) and is
/// retried with a back-off capped at <c>EvidenceMaxRetryAfter</c>. Evidence past its retention is deleted, only once
/// preserved.
/// </summary>
public sealed class ClipEvidenceProcessor(
    IClipEvidenceStore store,
    IEvidenceStorage evidence,
    IEvidenceQuarantine quarantine,
    IEvidenceMediaDeleter media,
    ICdnPurger cdn,
    IOptionsMonitor<ModerationOptions> options,
    ILogger<ClipEvidenceProcessor> logger)
{
    public const string VideoFile = "video.mp4";
    public const string SourceFile = "source";
    public const string RecordFile = "record.json";
    private const int ExpiredPerPass = 20;

    public static string Prefix(Guid evidenceId) => $"{evidenceId}/";

    /// <summary>
    /// How long a record waits after its <paramref name="attempts"/>th attempt failed: <c>EvidenceRetryAfter</c>, then,
    /// past <c>EvidenceMaxAttempts</c>, doubling up to <c>EvidenceMaxRetryAfter</c>.
    /// </summary>
    public static TimeSpan RetryDelay(int attempts, ModerationOptions current)
    {
        int over = attempts - current.EvidenceMaxAttempts;
        if (over < 0)
        {
            return current.EvidenceRetryAfter;
        }

        double scaled = current.EvidenceRetryAfter.TotalSeconds * Math.Pow(2, Math.Min(over + 1, 30));
        return TimeSpan.FromSeconds(Math.Min(scaled, current.EvidenceMaxRetryAfter.TotalSeconds));
    }

    /// <summary>Deletes evidence whose retention is over (preserved records only): its files, then its record.</summary>
    public async Task ExpireAsync()
    {
        foreach (Guid id in await store.GetExpired(ExpiredPerPass))
        {
            await evidence.DeletePrefixAsync(Prefix(id));
            await quarantine.DeleteAsync(id);
            await store.Delete(id);
            logger.LogInformation("Deleted the evidence {EvidenceId}: its retention is over", id);
        }
    }

    /// <summary>Works on the next record that needs it. True when one was claimed.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        ModerationOptions current = options.CurrentValue;
        if (await store.Claim(current.EvidenceStaleAfter) is { } pending)
        {
            await RunAsync(pending, claim => PreserveAsync(pending, claim, cancellationToken), current, preserving: true);
            return true;
        }

        if (await store.ClaimSweep(current.SweepAfter, current.EvidenceStaleAfter) is { } due)
        {
            await RunAsync(due, claim => SweepAsync(due, claim, cancellationToken), current, preserving: false);
            return true;
        }

        return false;
    }

    private async Task RunAsync(ClipEvidence record, Func<ClipEvidenceClaim, Task> work, ModerationOptions current,
        bool preserving)
    {
        // Taken once, as the claim handed it out: every write of this run carries it.
        ClipEvidenceClaim claim = record.ClaimToken;
        try
        {
            await work(claim);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the claim goes stale and the record is claimed again.
            throw;
        }
        catch (ClaimLostException)
        {
            logger.LogWarning("Evidence {EvidenceId} of clip {ClipId} was claimed again after this claim went stale; " +
                              "leaving it to that claim", record.Id, record.ClipId);
        }
        catch (Exception error)
        {
            bool needsAttention = preserving && record.Attempts >= current.EvidenceMaxAttempts;
            TimeSpan delay = preserving ? RetryDelay(record.Attempts, current) : current.EvidenceRetryAfter;
            if (needsAttention)
            {
                logger.LogError(error,
                    "Evidence {EvidenceId} of clip {ClipId} still isn't preserved after {Attempts} attempts and needs " +
                    "attention; nothing of the clip is deleted until it is (its files stay quarantined). " +
                    "Retrying in {Delay}. See reelshelf-docs/moderation.md",
                    record.Id, record.ClipId, record.Attempts, delay);
            }
            else
            {
                logger.LogError(error, "Evidence {EvidenceId} of clip {ClipId} ({Status}, attempt {Attempt}) failed; " +
                                       "retrying in {Delay}", record.Id, record.ClipId, record.Status, record.Attempts,
                    delay);
            }

            await store.Fail(claim, error.Message, delay, needsAttention);
        }
    }

    private async Task PreserveAsync(ClipEvidence record, ClipEvidenceClaim claim, CancellationToken cancellationToken)
    {
        EvidenceClip clip = record.ReadClip();

        // 1. Off public access first, without deleting the only copy, whatever state the evidence bucket is in.
        if (record.CdnPurge is null)
        {
            await quarantine.QuarantineAsync(record.Id, clip.Id, cancellationToken);
            Held(await store.SaveCdnPurge(claim, await PurgeAsync(clip, cancellationToken)));
        }

        // 2. The evidence copy.
        if (record.Files is null)
        {
            List<string> keys = [await UploadRecordAsync(record, cancellationToken)];
            if (await CopyVideoAsync(record.Id, clip, cancellationToken) is { } videoKey)
            {
                keys.Add(videoKey);
            }

            Held(await store.SaveFiles(claim, keys));
        }

        // 3. Only now that the copy exists is anything deleted for good.
        if (record.MediaDeletedAt is null)
        {
            await media.DeleteAsync(clip);
            await quarantine.DeleteAsync(record.Id);
            Held(await store.MarkMediaDeleted(claim));
        }

        Held(await store.MarkPreserved(claim));
        logger.LogInformation("Clip {ClipId} is preserved as evidence {EvidenceId} and gone from public storage",
            clip.Id, record.Id);
    }

    /// <summary>
    /// Late worker uploads under <c>clips/{clipId}/</c>: quarantined while the record isn't preserved, so nothing is
    /// deleted before the copy exists, and deleted once it is.
    /// </summary>
    private async Task SweepAsync(ClipEvidence record, ClipEvidenceClaim claim, CancellationToken cancellationToken)
    {
        EvidenceClip clip = record.ReadClip();
        if (record.Status == ClipEvidenceStatuses.Preserved)
        {
            await media.SweepAsync(clip.Id);
        }
        else
        {
            await quarantine.QuarantineAsync(record.Id, clip.Id, cancellationToken);
        }

        await cdn.PurgeClipAsync(clip.Id, MaxVersion(clip), cancellationToken);
        Held(await store.MarkSwept(claim));
    }

    private async Task<string> PurgeAsync(EvidenceClip clip, CancellationToken cancellationToken)
    {
        string purge = await cdn.PurgeClipAsync(clip.Id, MaxVersion(clip), cancellationToken);
        if (purge == CdnPurgeResults.NotConfigured)
        {
            logger.LogWarning("Clip {ClipId} was removed but the media domain wasn't purged: set Moderation:Cloudflare, " +
                              "or purge its URLs by hand", clip.Id);
        }

        return purge;
    }

    /// <summary>Stops the work when a step write was dropped because the claim is no longer current.</summary>
    private static void Held(bool written)
    {
        if (!written)
        {
            throw new ClaimLostException();
        }
    }

    private sealed class ClaimLostException : Exception;

    /// <summary>A trim running at removal writes the version after the clip's own.</summary>
    private static int MaxVersion(EvidenceClip clip) => clip.VideoVersion + 1;

    /// <summary>The snapshots and the decision, so the bucket holds everything even if the database is lost.</summary>
    private async Task<string> UploadRecordAsync(ClipEvidence record, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new
        {
            evidence_id = record.Id,
            clip_id = record.ClipId,
            owner_id = record.OwnerId,
            reason = record.Reason,
            note = record.Note,
            removed_at = record.RemovedAt,
            delete_after = record.DeleteAfter,
            clip = JsonDocument.Parse(record.Clip).RootElement,
            owner = JsonDocument.Parse(record.Owner).RootElement
        }, new JsonSerializerOptions { WriteIndented = true });
        string key = Prefix(record.Id) + RecordFile;
        await using MemoryStream content = new(Encoding.UTF8.GetBytes(json));
        await evidence.UploadAsync(key, content, "application/json", cancellationToken);
        return key;
    }

    /// <summary>
    /// The clip's video: its current <c>av1.mp4</c> from the quarantine, or, when it never finished encoding, its
    /// uploaded source. Null when there is none.
    /// </summary>
    private async Task<string?> CopyVideoAsync(Guid evidenceId, EvidenceClip clip, CancellationToken cancellationToken)
    {
        if (clip.VideoHost == VideoHostNames.R2)
        {
            if (clip.VideoStatus == ClipVideoStatus.Ready && await quarantine.OpenAsync(evidenceId,
                    R2ObjectKeys.Output(clip.Id, clip.VideoVersion, R2ObjectKeys.Av1), cancellationToken) is { } av1)
            {
                await using (av1)
                {
                    return await UploadAsync(evidenceId, VideoFile, av1, "video/mp4", cancellationToken);
                }
            }

            if (await quarantine.OpenAsync(evidenceId, R2ObjectKeys.Upload(clip.Id), cancellationToken) is { } source)
            {
                await using (source)
                {
                    return await UploadAsync(evidenceId, SourceFile, source, "application/octet-stream",
                        cancellationToken);
                }
            }

            logger.LogWarning("Clip {ClipId} had no video in the quarantine to preserve; only its record is kept", clip.Id);
            return null;
        }

        logger.LogWarning("Clip {ClipId} was on {VideoHost}, which no longer exists; only its record is kept",
            clip.Id, clip.VideoHost);
        return null;
    }

    private async Task<string> UploadAsync(Guid evidenceId, string file, Stream content, string contentType,
        CancellationToken cancellationToken)
    {
        string key = Prefix(evidenceId) + file;
        await evidence.UploadAsync(key, content, contentType, cancellationToken);
        return key;
    }
}

/// <summary>Lets a removal start the processor straight away instead of waiting for the next poll.</summary>
public sealed class ClipEvidenceSignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _wake.WaitAsync(timeout, cancellationToken);
}

/// <summary>Runs <see cref="ClipEvidenceProcessor"/> where the evidence bucket is set; never in <c>OpenApi</c>.</summary>
public sealed class ClipEvidenceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IEvidenceStorage evidence,
    ClipEvidenceSignal signal,
    IOptionsMonitor<ModerationOptions> options,
    ILogger<ClipEvidenceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!evidence.IsConfigured)
        {
            logger.LogInformation("Removing clips is off: Moderation:EvidenceBucket isn't set");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool busy = false;
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                ClipEvidenceProcessor processor = scope.ServiceProvider.GetRequiredService<ClipEvidenceProcessor>();
                await processor.ExpireAsync();
                busy = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "The evidence service failed; trying again shortly");
            }

            if (!busy)
            {
                try
                {
                    await signal.WaitAsync(options.CurrentValue.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
