using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Exceptions;

namespace Reelshelf.ApexLegends.LegendDetection;

public static class ApexDetectionEndpoints
{
    public static void MapApexDetectionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("apexdetection");

        group.MapPost("enqueue", QueueDetection).WithName("QueueDetection");
        group.MapPost("enqueue-all", QueueAllUnprocessedItems).WithName("QueueAllUnprocessedItems");
    }

    public static async Task<Results<Ok, BadRequest<string>>> QueueDetection(
        ApexDetectionWorkflow workflow,
        VideoDetectionRequest request)
    {
        try
        {
            await workflow.QueueDetection(request.ClipId, request.ScreenshotUrls);
            return TypedResults.Ok();
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    public static async Task<Results<Ok, BadRequest<string>>> QueueAllUnprocessedItems(
        ApexDetectionWorkflow workflow)
    {
        try
        {
            await workflow.QueueAllUnprocessedItems();
            return TypedResults.Ok();
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}

public sealed record VideoDetectionRequest(Guid ClipId, List<string> ScreenshotUrls);
