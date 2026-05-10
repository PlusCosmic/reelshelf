using Reelshelf.ApexLegends.Models;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.ApexLegends.LegendDetection;

public class ApexDetectionWorkflow(
    IApexDetectionQueueService queueService,
    ApexStatements apexStatements,
    GameCategoryStatements gameCategoryStatements)
{
    private const string ApexLegendsSlug = "apex-legends";
    private const int MaxScreenshotsPerRequest = 10;
    private const string BunnyCdnBaseUrl = "https://vz-cd8f9809-39a.b-cdn.net";

    public async Task QueueDetection(Guid clipId, List<string>? screenshotUrls)
    {
        if (screenshotUrls?.Any() != true)
        {
            throw new BadRequestException("No screenshot URLs provided");
        }

        if (screenshotUrls.Count > MaxScreenshotsPerRequest)
        {
            throw new BadRequestException("Maximum 10 screenshots allowed per request");
        }

        await queueService.QueueDetectionAsync(clipId, screenshotUrls);
    }

    public async Task QueueAllUnprocessedItems()
    {
        GameCategory? apexCategory = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apexCategory is null)
        {
            throw new BadRequestException("Apex Legends category not found");
        }

        List<ApexStatements.ApexClipDetectionRow> allDetections = await apexStatements.GetAllApexClipDetections();
        List<ApexStatements.ClipForDetectionRow> allClips = await apexStatements.GetClipsForCategory(apexCategory.Id);
        Dictionary<Guid, ApexStatements.ClipForDetectionRow> clipsById = allClips.ToDictionary(clip => clip.Id);

        List<Guid> clipsToProcess = GetClipsToProcess(allClips, allDetections);

        foreach (Guid clipId in clipsToProcess)
        {
            if (!clipsById.TryGetValue(clipId, out ApexStatements.ClipForDetectionRow? clipRow))
            {
                continue;
            }

            await apexStatements.DeleteApexClipDetection(clipId);
            await apexStatements.InsertApexClipDetection(clipId, (int)ClipDetectionStatus.NotStarted);
            await queueService.QueueDetectionAsync(clipId, GetScreenshotUrlsForVideo(clipRow.VideoId));
        }
    }

    private static List<Guid> GetClipsToProcess(
        List<ApexStatements.ClipForDetectionRow> allClips,
        List<ApexStatements.ApexClipDetectionRow> allDetections)
    {
        HashSet<Guid> detectedClipIds = allDetections.Select(detection => detection.ClipId).ToHashSet();
        IEnumerable<Guid> unprocessedClipIds = allClips
            .Select(clip => clip.Id)
            .Where(clipId => !detectedClipIds.Contains(clipId));

        IEnumerable<Guid> noneDetectionClipIds = allDetections
            .Where(detection => detection.PrimaryDetection == (int)ApexLegend.None)
            .Where(detection => detection.Status == (int)ClipDetectionStatus.Completed)
            .Select(detection => detection.ClipId);

        return unprocessedClipIds.Concat(noneDetectionClipIds).Distinct().ToList();
    }

    private static List<string> GetScreenshotUrlsForVideo(Guid videoId)
    {
        string videoPath = videoId.ToString();

        return
        [
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail.jpg",
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail_1.jpg",
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail_2.jpg",
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail_3.jpg",
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail_4.jpg",
            $"{BunnyCdnBaseUrl}/{videoPath}/thumbnail_5.jpg"
        ];
    }
}
