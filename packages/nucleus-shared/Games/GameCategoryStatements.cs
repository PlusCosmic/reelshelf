using Dapper;
using Npgsql;

namespace Nucleus.Shared.Games;

public class GameCategoryStatements(NpgsqlConnection connection)
{
    public async Task<List<GameCategory>> GetAllCategoriesAsync()
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            FROM game_category
            ORDER BY name
            """;
        var results = await connection.QueryAsync<GameCategory>(sql);
        return results.ToList();
    }

    public async Task<List<GameCategory>> GetUserCategoriesAsync(Guid userId)
    {
        const string sql = """
            SELECT gc.id, gc.igdb_id, gc.name, gc.slug, gc.cover_url, gc.key_art_url, gc.game_logo_url, gc.is_custom,
                   gc.created_at, gc.updated_at
            FROM game_category gc
            INNER JOIN user_game_category ugc ON gc.id = ugc.game_category_id
            WHERE ugc.user_id = @UserId
            ORDER BY gc.name
            """;
        var results = await connection.QueryAsync<GameCategory>(sql, new { UserId = userId });
        return results.ToList();
    }

    public async Task<GameCategory?> GetByIdAsync(Guid id)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            FROM game_category
            WHERE id = @Id
            """;
        return await connection.QuerySingleOrDefaultAsync<GameCategory>(sql, new { Id = id });
    }

    public async Task<GameCategory?> GetBySlugAsync(string slug)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            FROM game_category
            WHERE slug = @Slug
            """;
        return await connection.QuerySingleOrDefaultAsync<GameCategory>(sql, new { Slug = slug });
    }

    public async Task<GameCategory?> GetByIgdbIdAsync(long igdbId)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            FROM game_category
            WHERE igdb_id = @IgdbId
            """;
        return await connection.QuerySingleOrDefaultAsync<GameCategory>(sql, new { IgdbId = igdbId });
    }

    public async Task<List<GameCategory>> GetCategoriesNeedingIgdbAssetRefreshAsync(
        int limit,
        TimeSpan staleAfter)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            FROM game_category
            WHERE igdb_id IS NOT NULL
              AND (key_art_url IS NULL OR game_logo_url IS NULL)
              AND updated_at <= now() - @StaleAfter
            ORDER BY updated_at, name
            LIMIT @Limit
            """;

        var results = await connection.QueryAsync<GameCategory>(sql, new { Limit = limit, StaleAfter = staleAfter });
        return results.ToList();
    }

    public async Task<GameCategory> CreateCategoryAsync(CreateGameCategoryRequest request)
    {
        const string sql = """
            INSERT INTO game_category (igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom)
            VALUES (@IgdbId, @Name, @Slug, @CoverUrl, @KeyArtUrl, @GameLogoUrl, @IsCustom)
            RETURNING id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            """;
        return await connection.QuerySingleAsync<GameCategory>(sql, new
        {
            request.IgdbId,
            request.Name,
            request.Slug,
            request.CoverUrl,
            request.KeyArtUrl,
            request.GameLogoUrl,
            IsCustom = request.IgdbId == null
        });
    }

    public async Task<GameCategory> UpdateIgdbAssetsAsync(Guid id, GameDetails gameDetails)
    {
        const string sql = """
            UPDATE game_category
            SET cover_url = COALESCE(@CoverUrl, cover_url),
                key_art_url = COALESCE(@KeyArtUrl, key_art_url),
                game_logo_url = COALESCE(@GameLogoUrl, game_logo_url),
                updated_at = now()
            WHERE id = @Id
            RETURNING id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, is_custom, created_at, updated_at
            """;

        return await connection.QuerySingleAsync<GameCategory>(sql, new
        {
            Id = id,
            gameDetails.CoverUrl,
            gameDetails.KeyArtUrl,
            gameDetails.GameLogoUrl
        });
    }

    public async Task AddUserCategoryAsync(Guid userId, Guid categoryId)
    {
        const string sql = """
            INSERT INTO user_game_category (user_id, game_category_id)
            VALUES (@UserId, @CategoryId)
            ON CONFLICT (user_id, game_category_id) DO NOTHING
            """;
        await connection.ExecuteAsync(sql, new { UserId = userId, CategoryId = categoryId });
    }

    public async Task RemoveUserCategoryAsync(Guid userId, Guid categoryId)
    {
        const string sql = """
            DELETE FROM user_game_category
            WHERE user_id = @UserId AND game_category_id = @CategoryId
            """;
        await connection.ExecuteAsync(sql, new { UserId = userId, CategoryId = categoryId });
    }
}
