using Reelshelf.Video;

namespace Reelshelf.Core;

public class ClipStatusRefreshService(
    ILogger<ClipStatusRefreshService> logger,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        await RefreshClipStatusesAsync();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshClipStatusesAsync();
        }
    }

    /// <summary>
    /// A clip created this long ago whose video was never uploaded is treated as abandoned and removed,
    /// releasing the storage it reserved for its owner.
    /// </summary>
    private static readonly TimeSpan AbandonedUploadAge = TimeSpan.FromHours(24);

    /// <summary>One pass: purge abandoned uploads, then refresh the clips whose host needs polling.</summary>
    internal async Task RefreshClipStatusesAsync()
    {
        await PurgeAbandonedUploadsAsync();

        logger.LogInformation("Refreshing clip statuses");

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ClipsStatements clipsStatements = scope.ServiceProvider.GetRequiredService<ClipsStatements>();
            ClipVideoHosts videoHosts = scope.ServiceProvider.GetRequiredService<ClipVideoHosts>();

            List<ClipsStatements.ClipRow> clipsNeedingUpdate = await clipsStatements.GetClipsNeedingStatusUpdate();

            if (clipsNeedingUpdate.Count == 0)
            {
                logger.LogInformation("No clips need status updates");
                return;
            }

            logger.LogInformation("Found {Count} clips needing status updates", clipsNeedingUpdate.Count);

            int updated = 0;
            int failed = 0;

            foreach (ClipsStatements.ClipRow clip in clipsNeedingUpdate)
            {
                try
                {
                    IClipVideoHost videoHost = videoHosts.For(clip.VideoHost);
                    if (!videoHost.NeedsStatusPolling)
                    {
                        // Its status reaches the row from our own worker's reports; polling would read the row back.
                        continue;
                    }

                    HostedVideoInfo? video = await videoHost.GetVideoAsync(clip.VideoId);
                    if (video == null)
                    {
                        logger.LogWarning("Video {VideoId} not found at the video host for clip {ClipId}", clip.VideoId, clip.Id);
                        failed++;
                        continue;
                    }

                    await clipsStatements.UpdateClipMetadata(clip.Id, video.Title, video);

                    updated++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update clip {ClipId} (video {VideoId})", clip.Id, clip.VideoId);
                    failed++;
                }
            }

            logger.LogInformation("Clip status refresh complete: {Updated} updated, {Failed} failed", updated, failed);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to refresh clip statuses");
        }
    }

    private async Task PurgeAbandonedUploadsAsync()
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ClipsStatements clipsStatements = scope.ServiceProvider.GetRequiredService<ClipsStatements>();
            ClipVideoHosts videoHosts = scope.ServiceProvider.GetRequiredService<ClipVideoHosts>();

            List<ClipsStatements.ClipRow> candidates =
                await clipsStatements.GetAbandonedClips(DateTimeOffset.UtcNow - AbandonedUploadAge);

            foreach (ClipsStatements.ClipRow clip in candidates)
            {
                try
                {
                    // Re-check with the host so a clip whose update notification was missed is refreshed rather than deleted.
                    // A host whose clips report through the app (R2) answers from the clip row itself, so its clip is
                    // abandoned only when the row still says nothing was uploaded.
                    IClipVideoHost videoHost = videoHosts.For(clip.VideoHost);
                    HostedVideoInfo? video = await videoHost.GetVideoAsync(clip.VideoId);
                    if (!IsAbandoned(video))
                    {
                        continue;
                    }

                    if (video is not null)
                    {
                        await videoHost.DeleteVideoAsync(clip.VideoId);
                    }

                    await clipsStatements.DeleteClip(clip.Id);
                    logger.LogInformation("Removed abandoned upload {ClipId} (video {VideoId}) for owner {OwnerId}",
                        clip.Id, clip.VideoId, clip.OwnerId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to purge abandoned upload {ClipId}", clip.Id);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to purge abandoned uploads");
        }
    }

    /// <summary>
    /// True when a reserved clip's video never received its bytes: the host has no such video, or it stores
    /// nothing and its status says nothing was uploaded.
    /// </summary>
    public static bool IsAbandoned(HostedVideoInfo? video)
    {
        return video is null || (video.StorageSize <= 0 && !video.Status.IsUploaded());
    }
}
