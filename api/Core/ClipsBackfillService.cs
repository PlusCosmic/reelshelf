using Reelshelf.Games;
using Reelshelf.Video;

namespace Reelshelf.Core;

public class ClipsBackfillService(
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    ClipVideoHosts videoHosts,
    ILogger<ClipsBackfillService> logger)
{
    private const string ApexLegendsSlug = "apex-legends";

    public async Task<BackfillResult> BackfillClipMetadataAsync()
    {
        GameCategory? apexCategory = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apexCategory is null)
        {
            logger.LogWarning("Apex Legends category not found in database");
            return new BackfillResult(0, 0, 0);
        }

        List<ClipsStatements.ClipRow> allClips = await clipsStatements.GetAllClipsForCategory(apexCategory.Id);

        if (allClips.Count == 0)
        {
            logger.LogInformation("No clips need backfilling");
            return new BackfillResult(0, 0, 0);
        }

        logger.LogInformation("Found {Count} clips needing backfill", allClips.Count);

        int successCount = 0;
        int failureCount = 0;

        foreach (ClipsStatements.ClipRow clip in allClips)
        {
            try
            {
                IClipVideoHost videoHost = videoHosts.For(clip.VideoHost);
                if (!videoHost.NeedsStatusPolling)
                {
                    // The clip row is already what this host knows; there is nothing to copy back onto it.
                    successCount++;
                    continue;
                }

                HostedVideoInfo? video = await videoHost.GetVideoAsync(clip.VideoId);

                if (video is null)
                {
                    logger.LogWarning("Video {VideoId} for clip {ClipId} not found at the video host",
                        clip.VideoId, clip.Id);
                    failureCount++;
                    continue;
                }

                await clipsStatements.UpdateClipMetadata(clip.Id, video.Title, video);

                successCount++;
                logger.LogInformation("Backfilled clip {ClipId} with video {VideoId}",
                    clip.Id, clip.VideoId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to backfill clip {ClipId} with video {VideoId}",
                    clip.Id, clip.VideoId);
                failureCount++;
            }
        }

        logger.LogInformation("Backfill completed: {SuccessCount} succeeded, {FailureCount} failed",
            successCount, failureCount);

        return new BackfillResult(allClips.Count, successCount, failureCount);
    }
}

public record BackfillResult(int TotalProcessed, int SuccessCount, int FailureCount);
