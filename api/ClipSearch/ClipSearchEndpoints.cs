using Reelshelf.Auth;
using Reelshelf.FeatureFlags;

namespace Reelshelf.ClipSearch;

public static class ClipSearchEndpoints
{
    public static void MapClipSearchEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("clips/search")
            .RequireAuthorization()
            .RequireFeature(FeatureFlagKeys.ClipSearch);

        group.MapGet("availability", GetAvailability).WithName("GetClipSearchAvailability")
            .RequireAuthorization();
        group.MapPost("", Search).WithName("SearchClips")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.ClipSearch);
    }

    /// <summary>Whether the caller can search by description, and why not when they can't.</summary>
    private static async Task<ClipSearchAvailability> GetAvailability(
        ClipSearchService service,
        AuthenticatedUser user)
    {
        return await service.GetAvailabilityAsync(user);
    }

    /// <summary>
    /// Searches the caller's own clips by a description ("funny clips from 2025 in Call of Duty"). Answers 403 when
    /// their plan has no transcripts, and 503 when this deployment has no model for it or the model can't be reached.
    /// </summary>
    private static async Task<ClipSearchResponse> Search(
        ClipSearchService service,
        AuthenticatedUser user,
        ClipSearchRequest request,
        CancellationToken cancellationToken)
    {
        return await service.SearchAsync(user, request, cancellationToken);
    }
}
