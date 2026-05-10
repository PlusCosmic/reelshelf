using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Core.Models;

namespace Reelshelf.Core;

public static class SharedClipsEndpoints
{
    public static void MapSharedClipsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("shared-clips/{token}", GetSharedClip)
            .WithName("GetSharedClip")
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<SharedClipResponse>, NotFound>> GetSharedClip(
        ClipService clipService,
        string token)
    {
        SharedClipResponse? sharedClip = await clipService.GetSharedClip(token);
        if (sharedClip is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(sharedClip);
    }
}
