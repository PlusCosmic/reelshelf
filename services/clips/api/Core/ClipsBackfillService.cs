using Nucleus.Clips.Bunny;
using Nucleus.Clips.Bunny.Models;
using Nucleus.Clips.Core.Models;
using Nucleus.Shared.Games;

namespace Nucleus.Clips.Core;

public class ClipsBackfillService(
    ClipsBackfillStatements backfillStatements,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    BunnyService bunnyService,
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
                BunnyVideo? bunnyVideo = await bunnyService.GetVideoByIdAsync(clip.VideoId);

                if (bunnyVideo is null)
                {
                    logger.LogWarning("Video {VideoId} for clip {ClipId} not found in Bunny CDN",
                        clip.VideoId, clip.Id);
                    failureCount++;
                    continue;
                }

                await backfillStatements.UpdateClipMetadataAsync(
                    clip.Id,
                    bunnyVideo.Title,
                    bunnyVideo.Length,
                    bunnyVideo.ThumbnailFileName,
                    bunnyVideo.DateUploaded,
                    bunnyVideo.StorageSize,
                    bunnyVideo.Status,
                    bunnyVideo.EncodeProgress
                );

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
