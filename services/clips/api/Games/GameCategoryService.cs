using System.Text.RegularExpressions;
using Nucleus.Shared.Discord;
using Nucleus.Shared.Games;

namespace Nucleus.Clips.Games;

public partial class GameCategoryService(
    Nucleus.Shared.Games.GameCategoryStatements statements,
    IgdbService igdbService,
    DiscordStatements discordStatements)
{
    public async Task<List<GameCategoryResponse>> GetAllCategoriesAsync()
    {
        var categories = await statements.GetAllCategoriesAsync();
        return categories.Select(ToResponse).ToList();
    }

    public async Task<List<GameCategoryResponse>> GetUserCategoriesAsync(string discordUserId)
    {
        var user = await discordStatements.GetUserByDiscordId(discordUserId);
        if (user == null) return [];

        var categories = await statements.GetUserCategoriesAsync(user.Id);
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
        string discordUserId,
        long igdbId)
    {
        var user = await discordStatements.GetUserByDiscordId(discordUserId);
        if (user == null) return null;

        // Check if category already exists globally
        var existing = await statements.GetByIgdbIdAsync(igdbId);
        if (existing != null)
        {
            // Just add the user subscription
            await statements.AddUserCategoryAsync(user.Id, existing.Id);
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
            gameDetails.CoverUrl
        ));

        // Add user subscription
        await statements.AddUserCategoryAsync(user.Id, category.Id);

        return ToResponse(category);
    }

    public async Task<GameCategoryResponse?> AddCustomCategoryAsync(
        string discordUserId,
        string name,
        string? coverUrl)
    {
        var user = await discordStatements.GetUserByDiscordId(discordUserId);
        if (user == null) return null;

        var slug = GenerateSlug(name);

        // Check if slug already exists
        var existing = await statements.GetBySlugAsync(slug);
        if (existing != null)
        {
            await statements.AddUserCategoryAsync(user.Id, existing.Id);
            return ToResponse(existing);
        }

        var category = await statements.CreateCategoryAsync(new CreateGameCategoryRequest(
            null, // No IGDB ID for custom categories
            name,
            slug,
            coverUrl
        ));

        await statements.AddUserCategoryAsync(user.Id, category.Id);
        return ToResponse(category);
    }

    public async Task<bool> RemoveUserCategoryAsync(string discordUserId, Guid categoryId)
    {
        var user = await discordStatements.GetUserByDiscordId(discordUserId);
        if (user == null) return false;

        await statements.RemoveUserCategoryAsync(user.Id, categoryId);
        return true;
    }

    private static GameCategoryResponse ToResponse(GameCategory category) =>
        new(category.Id, category.Name, category.Slug, category.CoverUrl, category.IsCustom);

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
