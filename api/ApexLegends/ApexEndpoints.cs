using Microsoft.AspNetCore.Http.HttpResults;

namespace Nucleus.Clips.ApexLegends;

public static class ApexEndpoints
{
    public static void MapApexEndpoints(this IEndpointRouteBuilder app)
    {
        // Map rotation is public - no auth required
        app.MapGet("apex-legends/map-rotation", async (MapService mapService) => await mapService.GetMapRotation())
            .WithName("GetApexMapRotation")
            .AllowAnonymous();

        // Other apex endpoints require authorization
        RouteGroupBuilder group = app.MapGroup("apex-legends").RequireAuthorization();
        group.MapPost("assign-account", AssignAccount).WithName("AssignAccount");
    }

    // Platform can be PS4, X1, PC
    public static Results<Ok, BadRequest> AssignAccount(string username, string platform)
    {
        return TypedResults.Ok();
    }
}