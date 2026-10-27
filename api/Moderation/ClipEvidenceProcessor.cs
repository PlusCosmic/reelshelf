using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.Plans;
using Reelshelf.Video;

namespace Reelshelf.Moderation;

/// <summary>
/// Preserves a clip removed after a child-safety or terrorism report, then takes it off public storage. The removal
/// itself already deleted the clip row, so nothing in Reelshelf lists or plays it; this copies the video into the
/// private evidence bucket, deletes it from its host, purges the media domain's cache, and a few hours later sweeps
/// the clip's R2 folder again for anything a transcode worker uploaded with URLs it already held. Each step is recorded
/// as it finishes and is safe to repeat, so a failed record is retried from where it stopped. Evidence past its
/// retention is deleted.
/// </summary>
public sealed class ClipEvidenceProcessor(
    IClipEvidenceStore store,
    IEvidenceStorage evidence,
    IExportVideoSource videos,
    IEvidenceMediaDeleter media,
    ICdnPurger cdn,
    IOptionsMonitor<ModerationOptions> options,
    ILogger<ClipEvidenceProcessor> logger)
{
    public const string VideoFile = "video.mp4";
    public const string RecordFile = "record.json";
    private const int ExpiredPerPass = 20;

    public static string Prefix(Guid evidenceId) => $"{evidenceId}/";

    /// <summary>Deletes evidence whose retention is over: its files, then its record.</summary>
    public async Task ExpireAsync()
    {
        foreach (Guid id in await store.GetExpired(ExpiredPerPass))
        {
            await evidence.DeletePrefixAsync(Prefix(id));
            await store.Delete(id);
            logger.LogInformation("Deleted the evidence {EvidenceId}: its retention is over", id);
        }
    }

    /// <summary>Works on the next record that needs it. True when one was claimed.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        ModerationOptions current = options.CurrentValue;
        if (await store.Claim(current.EvidenceStaleAfter, current.EvidenceMaxAttempts) is { } pending)
        {
            await RunAsync(pending, () => PreserveAsync(pending, cancellationToken), current);
            return true;
        }

        if (await store.ClaimSweep(current.SweepAfter, current.EvidenceStaleAfter) is { } preserved)
        {
            await RunAsync(preserved, () => SweepAsync(preserved, cancellationToken), current);
            return true;
        }

        return false;
    }

    private async Task RunAsync(ClipEvidence record, Func<Task> work, ModerationOptions current)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the claim goes stale and the record is claimed again.
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Evidence {EvidenceId} of clip {ClipId} failed (attempt {Attempt}); it will be retried",
                record.Id, record.ClipId, record.Attempts);
            await store.Fail(record.Id, error.Message, current.EvidenceRetryAfter);
        }
    }

    private async Task PreserveAsync(ClipEvidence record, CancellationToken cancellationToken)
    {
        EvidenceClip clip = record.ReadClip();

        // Copied before anything is deleted, so a failure never loses the only copy.
        if (record.Files is null)
        {
            List<string> keys = [await UploadRecordAsync(record, cancellationToken)];
            if (await CopyVideoAsync(record.Id, clip, cancellationToken) is { } videoKey)
            {
                keys.Add(videoKey);
            }

            await store.SaveFiles(record.Id, keys);
        }

        if (record.MediaDeletedAt is null)
        {
            await media.DeleteAsync(clip);
            await store.MarkMediaDeleted(record.Id);
        }

        if (record.CdnPurge is null)
        {
            string purge = await cdn.PurgeClipAsync(clip.Id, MaxVersion(clip), cancellationToken);
            if (purge == CdnPurgeResults.NotConfigured)
            {
                logger.LogWarning("Clip {ClipId} was removed but the media domain wasn't purged: set Moderation:Cloudflare, " +
                                  "or purge its URLs by hand", clip.Id);
            }

            await store.SaveCdnPurge(record.Id, purge);
        }

        await store.MarkPreserved(record.Id);
        logger.LogInformation("Clip {ClipId} is preserved as evidence {EvidenceId} and gone from public storage",
            clip.Id, record.Id);
    }

    private async Task SweepAsync(ClipEvidence record, CancellationToken cancellationToken)
    {
        EvidenceClip clip = record.ReadClip();
        await media.SweepAsync(clip.Id);
        await cdn.PurgeClipAsync(clip.Id, MaxVersion(clip), cancellationToken);
        await store.MarkSwept(record.Id);
    }

    /// <summary>A <c>migrate</c> job running at removal writes the version after the clip's own.</summary>
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

    /// <summary>The clip's current video, as an export would read it; null when it never finished encoding.</summary>
    private async Task<string?> CopyVideoAsync(Guid evidenceId, EvidenceClip clip, CancellationToken cancellationToken)
    {
        ExportClipRow row = new()
        {
            Id = clip.Id,
            Title = clip.Title,
            CreatedAt = clip.CreatedAt,
            GameName = clip.GameName,
            StorageSize = clip.StorageSize,
            VideoHost = clip.VideoHost,
            VideoId = clip.VideoId,
            VideoVersion = clip.VideoVersion,
            VideoCodecs = clip.VideoCodecs,
            VideoHeight = clip.VideoHeight,
            VideoState = clip.VideoState
        };
        await using ExportVideo? video = await videos.OpenAsync(row, cancellationToken);
        if (video is null)
        {
            logger.LogWarning("Clip {ClipId} had no finished video to preserve; only its record is kept", clip.Id);
            return null;
        }

        string key = Prefix(evidenceId) + VideoFile;
        await evidence.UploadAsync(key, video.Content, "video/mp4", cancellationToken);
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
