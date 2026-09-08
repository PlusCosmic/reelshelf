using Reelshelf.Bunny;
using Reelshelf.Bunny.Models;

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

    private async Task RefreshClipStatusesAsync()
    {
        await PurgeAbandonedUploadsAsync();

        logger.LogInformation("Refreshing clip statuses");

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ClipsStatements clipsStatements = scope.ServiceProvider.GetRequiredService<ClipsStatements>();
            BunnyService bunnyService = scope.ServiceProvider.GetRequiredService<BunnyService>();

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
                    BunnyVideo? video = await bunnyService.GetVideoByIdAsync(clip.VideoId);
                    if (video == null)
                    {
                        logger.LogWarning("Video {VideoId} not found in Bunny CDN for clip {ClipId}", clip.VideoId, clip.Id);
                        failed++;
                        continue;
                    }

                    await clipsStatements.UpdateClipMetadata(
                        clip.Id,
                        video.Title,
                        video.Length,
                        video.ThumbnailFileName,
                        video.DateUploaded,
                        video.StorageSize,
                        video.Status,
                        video.EncodeProgress
                    );

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
            BunnyService bunnyService = scope.ServiceProvider.GetRequiredService<BunnyService>();

            List<ClipsStatements.ClipRow> candidates =
                await clipsStatements.GetAbandonedClips(DateTimeOffset.UtcNow - AbandonedUploadAge);

            foreach (ClipsStatements.ClipRow clip in candidates)
            {
                try
                {
                    // Re-check with Bunny so a clip whose webhook was missed is refreshed rather than deleted.
                    BunnyVideo? video = await bunnyService.GetVideoByIdAsync(clip.VideoId);
                    if (video is not null && (video.StorageSize > 0 || IsUploaded(video.Status)))
                    {
                        continue;
                    }

                    if (video is not null)
                    {
                        await bunnyService.DeleteVideoAsync(clip.VideoId);
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
    /// True for statuses that mean bytes reached Bunny and are being (or have been) processed. Failed is
    /// deliberately excluded: a failed video with zero storage is abandoned, and one with storage is kept by
    /// the storage check that runs before this.
    /// </summary>
    private static bool IsUploaded(int status)
    {
        return status is not ((int)BunnyVideoStatus.Queued
            or (int)BunnyVideoStatus.PresignedUploadStarted
            or (int)BunnyVideoStatus.PresignedUploadFailed
            or (int)BunnyVideoStatus.Failed);
    }
}
