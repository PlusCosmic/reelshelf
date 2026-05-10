using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.ApexLegends.Models;
using Reelshelf.Games;

namespace Reelshelf.ApexLegends.LegendDetection;

public static class ApexDetectionEndpoints
{
    private const string ApexLegendsSlug = "apex-legends";

    public static void MapApexDetectionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("apexdetection");

        group.MapPost("enqueue", QueueDetection).WithName("QueueDetection");
        group.MapPost("enqueue-all", QueueAllUnprocessedItems).WithName("QueueAllUnprocessedItems");
    }

    public static async Task<Results<Ok, BadRequest<string>>> QueueDetection(
        IApexDetectionQueueService queueService,
        VideoDetectionRequest request)
    {
        if (request.ScreenshotUrls?.Any() != true)
        {
            return TypedResults.BadRequest("No screenshot URLs provided");
        }

        if (request.ScreenshotUrls.Count > 10)
        {
            return TypedResults.BadRequest("Maximum 10 screenshots allowed per request");
        }

        await queueService.QueueDetectionAsync(
            request.ClipId,
            request.ScreenshotUrls);

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, BadRequest<string>>> QueueAllUnprocessedItems(
        IApexDetectionQueueService queueService,
        ApexStatements apexStatements,
        GameCategoryStatements gameCategoryStatements)
    {
        GameCategory? apexCategory = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apexCategory is null)
        {
            return TypedResults.BadRequest("Apex Legends category not found");
        }

        List<ApexStatements.ApexClipDetectionRow> allDetections = await apexStatements.GetAllApexClipDetections();
        List<ApexStatements.ClipForDetectionRow> allClips = await apexStatements.GetClipsForCategory(apexCategory.Id);
        List<Guid> allClipIds = allClips.Select(c => c.Id).ToList();
        List<Guid> allDetectionIds = allDetections.Select(d => d.ClipId).ToList();
        List<Guid> unprocessedClipIds = allClipIds.Except(allDetectionIds).ToList();

        List<Guid> noneDetectionClipIds = allDetections
            .Where(d => d.PrimaryDetection == 27)
            .Where(d => d.Status == (int)ClipDetectionStatus.Completed)
            .Select(d => d.ClipId)
            .ToList();

        foreach (Guid clipId in noneDetectionClipIds)
        {
            await apexStatements.DeleteApexClipDetection(clipId);
        }

        List<Guid> clipsToProcess = unprocessedClipIds.Concat(noneDetectionClipIds).ToList();

        foreach (Guid clipId in clipsToProcess)
        {
            ApexStatements.ClipForDetectionRow clipRow = allClips.First(c => c.Id == clipId);
            await apexStatements.InsertApexClipDetection(clipId, 0);
            await queueService.QueueDetectionAsync(clipId, GetScreenshotUrlsForVideo(clipRow.VideoId));
        }

        return TypedResults.Ok();
    }

    private static List<string> GetScreenshotUrlsForVideo(Guid videoId)
    {
        return
        [
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail.jpg",
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail_1.jpg",
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail_2.jpg",
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail_3.jpg",
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail_4.jpg",
            $"https://vz-cd8f9809-39a.b-cdn.net/{videoId.ToString()}/thumbnail_5.jpg"
        ];
    }
}

public sealed record VideoDetectionRequest(Guid ClipId, List<string> ScreenshotUrls);
