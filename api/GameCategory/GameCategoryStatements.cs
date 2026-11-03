using Dapper;
using Npgsql;

namespace Reelshelf.Games;

public class GameCategoryStatements(NpgsqlConnection connection)
{
    public async Task<List<GameCategory>> GetAllCategoriesAsync()
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            FROM game_category
            ORDER BY name
            """;
        var results = await connection.QueryAsync<GameCategory>(sql);
        return results.ToList();
    }

    public async Task<List<GameCategory>> GetUserCategoriesAsync(Guid userId)
    {
        const string sql = """
            SELECT gc.id, gc.igdb_id, gc.name, gc.slug, gc.cover_url, gc.key_art_url, gc.game_logo_url, gc.cloth_color, gc.is_custom,
                   gc.created_at, gc.updated_at
            FROM game_category gc
            INNER JOIN user_game_category ugc ON gc.id = ugc.game_category_id
            WHERE ugc.user_id = @UserId
            ORDER BY gc.name
            """;
        var results = await connection.QueryAsync<GameCategory>(sql, new { UserId = userId });
        return results.ToList();
    }

    /// <summary>
    /// Categories that belong in a user's library: the ones they added, plus any they already have clips in.
    /// Never the global list, so one account's custom categories do not appear in (or slow down) everyone's library.
    /// In shelf order: the books the owner arranged, then the rest by name.
    /// </summary>
    public async Task<List<GameCategory>> GetLibraryCategoriesAsync(Guid userId)
    {
        const string sql = """
            SELECT gc.id, gc.igdb_id, gc.name, gc.slug, gc.cover_url, gc.key_art_url, gc.game_logo_url, gc.cloth_color, gc.is_custom,
                   gc.created_at, gc.updated_at
            FROM game_category gc
            LEFT JOIN user_game_category ugc ON ugc.game_category_id = gc.id AND ugc.user_id = @UserId
            WHERE ugc.user_id IS NOT NULL
               OR EXISTS (SELECT 1 FROM clip c
                          WHERE c.game_category_id = gc.id AND c.owner_id = @UserId AND c.binned_at IS NULL)
            ORDER BY ugc.shelf_position NULLS LAST, gc.name, gc.id
            """;
        var results = await connection.QueryAsync<GameCategory>(sql, new { UserId = userId });
        return results.ToList();
    }

    /// <summary>
    /// Puts the owner's books in the given order, left to right, in one transaction. A book listed here that is on
    /// the shelf only because it holds clips gets a row; the owner's other rows lose their position and follow.
    /// </summary>
    public async Task SetShelfOrderAsync(Guid userId, IReadOnlyList<Guid> categoryIds)
    {
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("""
            UPDATE user_game_category SET shelf_position = NULL
            WHERE user_id = @UserId AND shelf_position IS NOT NULL AND NOT (game_category_id = ANY(@Ids))
            """, new { UserId = userId, Ids = categoryIds.ToArray() }, transaction);
        await connection.ExecuteAsync("""
            INSERT INTO user_game_category (user_id, game_category_id, shelf_position)
            SELECT @UserId, ids.id, (ids.ord - 1)::int
            FROM unnest(@Ids) WITH ORDINALITY AS ids(id, ord)
            -- Still on the shelf: a book another tab took off since the order was checked stays off.
            WHERE EXISTS (SELECT 1 FROM user_game_category ugc
                          WHERE ugc.user_id = @UserId AND ugc.game_category_id = ids.id)
               OR EXISTS (SELECT 1 FROM clip c WHERE c.owner_id = @UserId AND c.game_category_id = ids.id)
            ON CONFLICT (user_id, game_category_id) DO UPDATE SET shelf_position = EXCLUDED.shelf_position
            """, new { UserId = userId, Ids = categoryIds.ToArray() }, transaction);
        await transaction.CommitAsync();
    }

    public async Task<GameCategory?> GetByIdAsync(Guid id)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            FROM game_category
            WHERE id = @Id
            """;
        return await connection.QuerySingleOrDefaultAsync<GameCategory>(sql, new { Id = id });
    }

    public async Task<List<GameCategory>> GetByIdsAsync(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            FROM game_category
            WHERE id = ANY(@Ids)
            """;
        var results = await connection.QueryAsync<GameCategory>(sql, new { Ids = ids.ToArray() });
        return results.ToList();
    }

    public async Task<GameCategory?> GetBySlugAsync(string slug)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            FROM game_category
            WHERE slug = @Slug
            """;
        return await connection.QuerySingleOrDefaultAsync<GameCategory>(sql, new { Slug = slug });
    }

    public async Task<GameCategory?> GetByIgdbIdAsync(long igdbId)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
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
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
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
            RETURNING id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
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
                -- A new cover needs its cloth colour worked out again.
                cloth_color = CASE WHEN @CoverUrl IS NOT NULL AND @CoverUrl IS DISTINCT FROM cover_url
                                   THEN NULL ELSE cloth_color END,
                key_art_url = COALESCE(@KeyArtUrl, key_art_url),
                game_logo_url = COALESCE(@GameLogoUrl, game_logo_url),
                updated_at = now()
            WHERE id = @Id
            RETURNING id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            """;

        return await connection.QuerySingleAsync<GameCategory>(sql, new
        {
            Id = id,
            gameDetails.CoverUrl,
            gameDetails.KeyArtUrl,
            gameDetails.GameLogoUrl
        });
    }

    /// <summary>IGDB-covered categories with no cloth colour yet, oldest first.</summary>
    public async Task<List<GameCategory>> GetCategoriesNeedingClothColorAsync(int limit)
    {
        const string sql = """
            SELECT id, igdb_id, name, slug, cover_url, key_art_url, game_logo_url, cloth_color, is_custom, created_at, updated_at
            FROM game_category
            WHERE cloth_color IS NULL
              AND cover_url LIKE 'https://images.igdb.com/%'
            ORDER BY created_at, name
            LIMIT @Limit
            """;

        var results = await connection.QueryAsync<GameCategory>(sql, new { Limit = limit });
        return results.ToList();
    }

    /// <summary>Records a cloth colour, unless the cover changed since it was worked out.</summary>
    public async Task SetClothColorAsync(Guid id, string coverUrl, string clothColor)
    {
        const string sql = """
            UPDATE game_category
            SET cloth_color = @ClothColor
            WHERE id = @Id AND cover_url = @CoverUrl
            """;
        await connection.ExecuteAsync(sql, new { Id = id, CoverUrl = coverUrl, ClothColor = clothColor });
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

    /// <summary>
    /// Takes an empty book off the owner's shelf. A book that still holds any of the owner's clips (a reserved
    /// upload included, binned ones not) stays, since the shelf shows every game with clips; the result says which
    /// happened. A binned clip restored later puts its book back on the shelf.
    /// </summary>
    public async Task<RemoveShelfBookResult> RemoveUserCategoryAsync(Guid userId, Guid categoryId)
    {
        const string sql = """
            WITH has_clips AS (
                SELECT EXISTS (SELECT 1 FROM clip WHERE owner_id = @UserId AND game_category_id = @CategoryId
                                AND binned_at IS NULL) AS found
            ),
            removed AS (
                DELETE FROM user_game_category
                WHERE user_id = @UserId AND game_category_id = @CategoryId
                  AND NOT (SELECT found FROM has_clips)
                RETURNING 1
            )
            SELECT (SELECT found FROM has_clips) AS has_clips, EXISTS (SELECT 1 FROM removed) AS removed
            """;
        var row = await connection.QuerySingleAsync<(bool HasClips, bool Removed)>(
            sql, new { UserId = userId, CategoryId = categoryId });
        return row.HasClips ? RemoveShelfBookResult.HasClips
            : row.Removed ? RemoveShelfBookResult.Removed
            : RemoveShelfBookResult.NotOnShelf;
    }
}

public enum RemoveShelfBookResult
{
    Removed,
    /// <summary>The owner has clips in it, so it stays on the shelf.</summary>
    HasClips,
    NotOnShelf
}
