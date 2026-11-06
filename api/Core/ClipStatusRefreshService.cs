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

    /// <summary>One pass: purge abandoned uploads.</summary>
    internal Task RefreshClipStatusesAsync() => PurgeAbandonedUploadsAsync();

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
                    // The host answers from the clip row itself (R2), so the clip is abandoned only when the row
                    // still says nothing was uploaded.
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
