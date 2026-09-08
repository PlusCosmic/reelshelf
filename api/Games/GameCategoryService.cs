using System.Text.RegularExpressions;

namespace Reelshelf.Games;

public partial class GameCategoryService(
    GameCategoryStatements statements,
    IgdbService igdbService)
{
    public async Task<List<GameCategoryResponse>> GetAllCategoriesAsync()
    {
        var categories = await statements.GetAllCategoriesAsync();
        return categories.Select(ToResponse).ToList();
    }

    public async Task<List<GameCategoryResponse>> GetUserCategoriesAsync(Guid userId)
    {
        var categories = await statements.GetUserCategoriesAsync(userId);
        return categories.Select(ToResponse).ToList();
    }

    public async Task<List<GameCategoryResponse>> GetLibraryCategoriesAsync(Guid userId)
    {
        var categories = await statements.GetLibraryCategoriesAsync(userId);
        return categories.Select(ToResponse).ToList();
    }

    public async Task<GameCategoryResponse?> GetCategoryByIdAsync(Guid categoryId)
    {
        var category = await statements.GetByIdAsync(categoryId);
        return category != null ? ToResponse(category) : null;
    }

    public async Task<List<GameSearchResult>> SearchGamesAsync(string query)
    {
        return await igdbService.SearchGamesAsync(query);
    }

    public async Task<GameCategoryResponse?> AddGameCategoryAsync(
        Guid userId,
        long igdbId)
    {
        // Check if category already exists globally
        var existing = await statements.GetByIgdbIdAsync(igdbId);
        if (existing != null)
        {
            if (existing.KeyArtUrl == null || existing.GameLogoUrl == null)
            {
                var existingGameDetails = await igdbService.GetGameByIdAsync(igdbId);
                if (existingGameDetails != null)
                {
                    existing = await statements.UpdateIgdbAssetsAsync(existing.Id, existingGameDetails);
                }
            }

            // Just add the user subscription
            await statements.AddUserCategoryAsync(userId, existing.Id);
            return ToResponse(existing);
        }

        // Fetch game details from IGDB
        var gameDetails = await igdbService.GetGameByIdAsync(igdbId);
        if (gameDetails == null) return null;

        // Create the global category
        var category = await statements.CreateCategoryAsync(new CreateGameCategoryRequest(
            gameDetails.IgdbId,
            gameDetails.Name,
            gameDetails.Slug,
            gameDetails.CoverUrl,
            gameDetails.KeyArtUrl,
            gameDetails.GameLogoUrl
        ));

        // Add user subscription
        await statements.AddUserCategoryAsync(userId, category.Id);

        return ToResponse(category);
    }

    public async Task<GameCategoryResponse?> AddCustomCategoryAsync(
        Guid userId,
        string name,
        string? coverUrl)
    {
        // Custom categories are private to their creator. Slugs are globally unique, so suffix the creator's
        // id: another account cannot pre-create a name and have this user silently subscribe to its row,
        // and a custom slug can never collide with an IGDB category's slug.
        var slug = $"{GenerateSlug(name)}-{userId.ToString("N")[..8]}";

        // Re-adding a category this user already created subscribes to their own row again
        var existing = await statements.GetBySlugAsync(slug);
        if (existing != null)
        {
            await statements.AddUserCategoryAsync(userId, existing.Id);
            return ToResponse(existing);
        }

        var category = await statements.CreateCategoryAsync(new CreateGameCategoryRequest(
            null, // No IGDB ID for custom categories
            name,
            slug,
            coverUrl
        ));

        await statements.AddUserCategoryAsync(userId, category.Id);
        return ToResponse(category);
    }

    public async Task<bool> RemoveUserCategoryAsync(Guid userId, Guid categoryId)
    {
        await statements.RemoveUserCategoryAsync(userId, categoryId);
        return true;
    }

    private static GameCategoryResponse ToResponse(GameCategory category) =>
        new(
            category.Id,
            category.Name,
            category.Slug,
            category.CoverUrl,
            category.KeyArtUrl,
            category.GameLogoUrl,
            category.IsCustom
        );

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = SlugRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
