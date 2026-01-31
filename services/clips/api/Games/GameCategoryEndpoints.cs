using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Shared.Auth;
using Nucleus.Shared.Games;

namespace Nucleus.Clips.Games;

public static class GameCategoryEndpoints
{
    public static void MapGameCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("games").RequireAuthorization();

        group.MapGet("/categories", GetUserCategories).WithName("GetUserGameCategories");
        group.MapGet("/categories/{categoryId:guid}", GetCategoryById).WithName("GetGameCategoryById");
        group.MapGet("/search", SearchGames).WithName("SearchGames");
        group.MapPost("/categories/from-igdb", AddFromIgdb).WithName("AddGameCategoryFromIgdb");
        group.MapPost("/categories/custom", AddCustomCategory).WithName("AddCustomCategory");
        group.MapDelete("/categories/{categoryId:guid}", RemoveCategory).WithName("RemoveGameCategory");
    }

    private static async Task<Ok<List<GameCategoryResponse>>>
        GetUserCategories(GameCategoryService service)
    {
        var categories = await service.GetAllCategoriesAsync();
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
        var category = await service.AddGameCategoryAsync(user.DiscordId, request.IgdbId);
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

        var category = await service.AddCustomCategoryAsync(user.DiscordId, request.Name, request.CoverUrl);
        if (category is null) return TypedResults.BadRequest("Failed to create category");

        return TypedResults.Ok(category);
    }

    private static async Task<Ok>
        RemoveCategory(GameCategoryService service, AuthenticatedUser user, Guid categoryId)
    {
        await service.RemoveUserCategoryAsync(user.DiscordId, categoryId);
        return TypedResults.Ok();
    }
}
