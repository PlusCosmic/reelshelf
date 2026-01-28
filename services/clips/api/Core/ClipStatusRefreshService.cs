using Nucleus.Clips.Bunny;
using Nucleus.Clips.Bunny.Models;

namespace Nucleus.Clips.Core;

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

    private async Task RefreshClipStatusesAsync()
    {
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
}
