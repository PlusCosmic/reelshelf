using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;

namespace Reelshelf.Games;

public static class GameCategoryEndpoints
{
    public static void MapGameCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("games").RequireAuthorization();

        group.MapGet("/categories", GetUserCategories).WithName("GetUserGameCategories")
            .RequireAuthorization(AuthorizationPolicies.CookieOrDevice);
        group.MapGet("/categories/{categoryId:guid}", GetCategoryById).WithName("GetGameCategoryById");
        // Both routes call IGDB with the application's shared credentials, so they carry a per-user limit.
        group.MapGet("/search", SearchGames).WithName("SearchGames")
            .RequireRateLimiting(RateLimitPolicies.Igdb);
        group.MapPost("/categories/from-igdb", AddFromIgdb).WithName("AddGameCategoryFromIgdb")
            .RequireRateLimiting(RateLimitPolicies.Igdb);
        group.MapPost("/categories/custom", AddCustomCategory).WithName("AddCustomCategory");
        group.MapDelete("/categories/{categoryId:guid}", RemoveCategory).WithName("RemoveGameCategory");
        group.MapPut("/categories/order", SetShelfOrder).WithName("SetShelfOrder");
    }

    private static async Task<Ok<List<GameCategoryResponse>>>
        GetUserCategories(GameCategoryService service, AuthenticatedUser user)
    {
        var categories = await service.GetLibraryCategoriesAsync(user.Id);
        return TypedResults.Ok(categories);
    }

    private static async Task<Results<Ok<GameCategoryResponse>, NotFound>>
        GetCategoryById(GameCategoryService service, Guid categoryId)
    {
        var category = await service.GetCategoryByIdAsync(categoryId);
        if (category is null) return TypedResults.NotFound();

        return TypedResults.Ok(category);
    }

    private static async Task<Results<Ok<List<GameSearchResult>>, BadRequest<string>>>
        SearchGames(GameCategoryService service, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return TypedResults.BadRequest("Query must be at least 2 characters");

        var results = await service.SearchGamesAsync(query);
        return TypedResults.Ok(results);
    }

    private static async Task<Results<Ok<GameCategoryResponse>, NotFound>>
        AddFromIgdb(GameCategoryService service, AuthenticatedUser user, AddGameFromIgdbRequest request)
    {
        var category = await service.AddGameCategoryAsync(user.Id, request.IgdbId);
        if (category is null) return TypedResults.NotFound();

        return TypedResults.Ok(category);
    }

    private static async Task<Results<Ok<GameCategoryResponse>, BadRequest<string>>>
        AddCustomCategory(
            GameCategoryService service,
            AuthenticatedUser user,
            AddCustomCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.BadRequest("Name is required");

        if (request.Name.Length > 100)
            return TypedResults.BadRequest("Name must be 100 characters or less");

        var category = await service.AddCustomCategoryAsync(user.Id, request.Name, request.CoverUrl);
        if (category is null) return TypedResults.BadRequest("Failed to create category");

        return TypedResults.Ok(category);
    }

    /// <summary>Takes an empty book off the shelf; 409 while it holds any of the caller's clips.</summary>
    private static async Task<Results<NoContent, Conflict<string>>>
        RemoveCategory(GameCategoryService service, AuthenticatedUser user, Guid categoryId)
    {
        var result = await service.RemoveUserCategoryAsync(user.Id, categoryId);
        if (result == RemoveShelfBookResult.HasClips)
            return TypedResults.Conflict("This book still has clips in it");

        // A book that was never on the shelf is already off it.
        return TypedResults.NoContent();
    }

    private const int MaxShelfBooks = 1000;

    /// <summary>Arranges the caller's shelf: every id a book on it, once each.</summary>
    private static async Task<Results<NoContent, BadRequest<string>>>
        SetShelfOrder(GameCategoryService service, AuthenticatedUser user, SetShelfOrderRequest request)
    {
        if (request.CategoryIds is not { Count: > 0 and <= MaxShelfBooks })
            return TypedResults.BadRequest("List the books on your shelf");

        if (!await service.SetShelfOrderAsync(user.Id, request.CategoryIds))
            return TypedResults.BadRequest("Every book must be on your shelf, once");

        return TypedResults.NoContent();
    }
}
