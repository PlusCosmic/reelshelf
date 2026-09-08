using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;

namespace Reelshelf.Twitch;

public static class TwitchClipsEndpoints
{
    private const int DefaultPageSize = 24;

    public static void MapTwitchClipsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("twitch/clips")
            .RequireAuthorization();

        group.MapGet("/", GetClips).WithName("GetTwitchClips");
        group.MapPost("/import", ImportClip).WithName("ImportTwitchClip")
            .RequirePermission(Permissions.ClipsCreate)
            .RequireRateLimiting(RateLimitPolicies.ClipPrepare);
    }

    /// <summary>
    /// The signed-in user's own Twitch clips, most viewed first within <paramref name="days"/> (all time when
    /// omitted). The response also says whether Twitch is linked and authorized, so the page can offer the
    /// right call to action instead of an error.
    /// </summary>
    private static async Task<Results<Ok<TwitchClipsResponse>, BadRequest<string>>> GetClips(
        TwitchClipService twitchClips,
        AuthenticatedUser user,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? days = null,
        int first = DefaultPageSize)
    {
        if (first < 1 || first > 100)
        {
            return TypedResults.BadRequest("Page size must be between 1 and 100");
        }

        if (days is < 1 or > 3650)
        {
            return TypedResults.BadRequest("Days must be between 1 and 3650");
        }

        return TypedResults.Ok(await twitchClips.GetClips(user.Id, first, cursor, days, cancellationToken));
    }

    private static async Task<Results<Ok<ImportedTwitchClipResponse>, Conflict<string>>> ImportClip(
        TwitchClipService twitchClips,
        AuthenticatedUser user,
        ImportTwitchClipRequest request,
        CancellationToken cancellationToken)
    {
        ImportedTwitchClipResponse? imported = await twitchClips.ImportClip(user.Id, request, cancellationToken);
        if (imported is null)
        {
            return TypedResults.Conflict("This Twitch clip is already in your library");
        }

        return TypedResults.Ok(imported);
    }
}
