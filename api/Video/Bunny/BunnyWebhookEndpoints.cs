using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Video.Bunny.Models;

namespace Reelshelf.Video.Bunny;

public static class BunnyWebhookEndpoints
{
    public static void MapBunnyWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("webhooks/bunny");
        group.MapPost("video-progress", ReceiveVideoProgress).WithName("ReceiveVideoProgress");
    }

    public static async Task<Results<Ok, UnauthorizedHttpResult>> ReceiveVideoProgress(
        VideoProgressUpdate update,
        ClipVideoUpdateHandler updateHandler,
        IConfiguration configuration,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("Reelshelf.Bunny.BunnyWebhookEndpoints");

        logger.LogInformation(
            "[WEBHOOK] Received Bunny webhook - VideoLibraryId: {LibraryId}, VideoGuid: {VideoGuid}, Status: {Status}, IP: {RemoteIp}, ContentType: {ContentType}",
            update.VideoLibraryId, update.VideoGuid, update.Status, context.Connection.RemoteIpAddress, context.Request.ContentType);

        // Validate webhook secret for security
        string? expectedSecret = configuration["BunnyWebhookSecret"];
        string? providedSecret = context.Request.Headers["X-Webhook-Secret"].FirstOrDefault()
                                 ?? context.Request.Query["secret"].FirstOrDefault();

        logger.LogDebug("[WEBHOOK] Security check - Expected secret configured: {HasSecret}, Secret provided: {ProvidedSecret}",
            !string.IsNullOrEmpty(expectedSecret), !string.IsNullOrEmpty(providedSecret));

        if (string.IsNullOrEmpty(expectedSecret))
        {
            logger.LogError("[WEBHOOK] BunnyWebhookSecret is not configured; rejecting webhook for VideoGuid: {VideoGuid}", update.VideoGuid);
            return TypedResults.Unauthorized();
        }

        if (!TimeConstantEquals(expectedSecret, providedSecret))
        {
            logger.LogWarning("[WEBHOOK] Unauthorized webhook attempt - invalid secret for VideoGuid: {VideoGuid}", update.VideoGuid);
            return TypedResults.Unauthorized();
        }

        await updateHandler.HandleAsync(update.VideoGuid);

        logger.LogInformation("[WEBHOOK] Webhook processing complete for VideoGuid: {VideoGuid}, returning OK", update.VideoGuid);
        return TypedResults.Ok();
    }

    private static bool TimeConstantEquals(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
