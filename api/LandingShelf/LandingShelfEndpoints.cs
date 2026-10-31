using Microsoft.AspNetCore.Http.HttpResults;

namespace Reelshelf.LandingShelf;

public static class LandingShelfEndpoints
{
    public static void MapLandingShelfEndpoints(this IEndpointRouteBuilder app)
    {
        // The signed-out landing page reads it, so it is public; it only ever serves the cached list.
        app.MapGet("landing/shelf", GetLandingShelf)
            .WithName("GetLandingShelf")
            .AllowAnonymous();
    }

    private static Ok<LandingShelfResponse> GetLandingShelf(LandingShelfCache cache, HttpContext httpContext)
    {
        LandingShelfResponse shelf = cache.Current;
        // An empty shelf is waiting on its first refresh, so let browsers ask again soon.
        httpContext.Response.Headers.CacheControl = shelf.Games.Count > 0 ? "public, max-age=3600" : "no-store";
        return TypedResults.Ok(shelf);
    }
}
