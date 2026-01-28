using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Clips.Bunny.Models;
using Nucleus.Clips.Core;

namespace Nucleus.Clips.Bunny;

public static class BunnyWebhookEndpoints
{
    public static void MapBunnyWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("webhooks/bunny");
        group.MapPost("video-progress", ReceiveVideoProgress).WithName("ReceiveVideoProgress");
        group.MapGet("test", TestWebhook).WithName("TestWebhook");
    }

    private static IResult TestWebhook(ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("Nucleus.Clips.Bunny.BunnyWebhookEndpoints");
        logger.LogInformation("[WEBHOOK] Test endpoint hit - webhook routing is working");
        return TypedResults.Ok(new { message = "Webhook endpoint is reachable", timestamp = DateTimeOffset.UtcNow });
    }

    public static async Task<Results<Ok, UnauthorizedHttpResult>> ReceiveVideoProgress(
        VideoProgressUpdate update,
        ClipsStatements clipsStatements,
        BunnyService bunnyService,
        ClipsBackfillStatements backfillStatements,
        IConfiguration configuration,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("Nucleus.Clips.Bunny.BunnyWebhookEndpoints");

        logger.LogInformation(
            "[WEBHOOK] Received Bunny webhook - VideoLibraryId: {LibraryId}, VideoGuid: {VideoGuid}, Status: {Status}, IP: {RemoteIp}, ContentType: {ContentType}",
            update.VideoLibraryId, update.VideoGuid, update.Status, context.Connection.RemoteIpAddress, context.Request.ContentType);

        // Validate webhook secret for security
        string? expectedSecret = configuration["BunnyWebhookSecret"];
        string? providedSecret = context.Request.Headers["X-Webhook-Secret"].FirstOrDefault()
                                 ?? context.Request.Query["secret"].FirstOrDefault();

        logger.LogDebug("[WEBHOOK] Security check - Expected secret configured: {HasSecret}, Secret provided: {ProvidedSecret}",
            !string.IsNullOrEmpty(expectedSecret), !string.IsNullOrEmpty(providedSecret));

        if (!string.IsNullOrEmpty(expectedSecret) && expectedSecret != providedSecret)
        {
            logger.LogWarning("[WEBHOOK] Unauthorized webhook attempt - invalid secret for VideoGuid: {VideoGuid}", update.VideoGuid);
            return TypedResults.Unauthorized();
        }

        logger.LogInformation("[WEBHOOK] Looking up clip by VideoGuid: {VideoGuid}", update.VideoGuid);
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipByVideoId(update.VideoGuid);
        if (clip == null)
        {
            logger.LogWarning("[WEBHOOK] No clip found for VideoGuid: {VideoGuid} - ignoring webhook", update.VideoGuid);
            return TypedResults.Ok();
        }

        logger.LogInformation("[WEBHOOK] Found clip - ClipId: {ClipId}, Title: {Title}",
            clip.Id, clip.Title);

        // fetch clip from bunny and update our model in the db
        logger.LogInformation("[WEBHOOK] Fetching video metadata from Bunny for VideoGuid: {VideoGuid}", update.VideoGuid);
        BunnyVideo? video = await bunnyService.GetVideoByIdAsync(update.VideoGuid);
        if (video == null)
        {
            logger.LogWarning("[WEBHOOK] Failed to fetch video from Bunny API for VideoGuid: {VideoGuid}", update.VideoGuid);
            return TypedResults.Ok();
        }

        logger.LogInformation(
            "[WEBHOOK] Fetched video from Bunny - Title: {Title}, Length: {Length}s, Status: {Status}, EncodeProgress: {Progress}%",
            video.Title, video.Length, video.Status, video.EncodeProgress);

        try
        {
            await backfillStatements.UpdateClipMetadataAsync(
                clip.Id,
                clip.Title ?? video.Title,
                video.Length,
                video.ThumbnailFileName,
                video.DateUploaded,
                video.StorageSize,
                video.Status,
                video.EncodeProgress);

            logger.LogInformation("[WEBHOOK] Successfully updated clip metadata for ClipId: {ClipId}", clip.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[WEBHOOK] Error updating clip metadata for ClipId: {ClipId}", clip.Id);
        }

        logger.LogInformation("[WEBHOOK] Webhook processing complete for VideoGuid: {VideoGuid}, returning OK", update.VideoGuid);
        return TypedResults.Ok();
    }
}
