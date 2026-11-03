using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Games;

/// <summary>Arranging the library shelf and taking empty books off it, against a real Postgres on V54.</summary>
public class ShelfOrderStatementsTests
{
    [PostgresFact]
    public async Task TheShelf_KeepsItsOrder_AndOnlyEmptyBooksComeOff()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"shelf_test_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        }

        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
            $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        try
        {
            await connection.ExecuteAsync("""
                CREATE TABLE game_category (
                    id uuid PRIMARY KEY,
                    igdb_id bigint,
                    name text NOT NULL,
                    slug text NOT NULL UNIQUE,
                    cover_url text,
                    key_art_url text,
                    game_logo_url text,
                    cloth_color text,
                    is_custom boolean NOT NULL DEFAULT false,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    updated_at timestamptz NOT NULL DEFAULT now()
                );
                CREATE TABLE user_game_category (
                    user_id uuid NOT NULL,
                    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
                    PRIMARY KEY (user_id, game_category_id)
                );
                """);
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V54__shelf_order.sql")));
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V56__game_category_art_copies.sql")));
            await RunAsync(connection, new GameCategoryStatements(dataSource.CreateConnection()));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, GameCategoryStatements statements)
    {
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        Guid apex = await AddCategory(connection, "Apex Legends");
        Guid celeste = await AddCategory(connection, "Celeste");
        Guid doom = await AddCategory(connection, "Doom");
        Guid elden = await AddCategory(connection, "Elden Ring");

        // Apex and Doom were added; Celeste is on the shelf only because it holds a clip; Elden Ring is someone else's.
        await statements.AddUserCategoryAsync(owner, apex);
        await statements.AddUserCategoryAsync(owner, doom);
        await InsertClip(connection, owner, celeste);
        await statements.AddUserCategoryAsync(other, elden);

        // Unarranged, the shelf reads by name.
        Assert.Equal([apex, celeste, doom], await Shelf(statements, owner));

        // Arranged, it keeps the owner's order, giving the clips-only book a row of its own.
        await statements.SetShelfOrderAsync(owner, [doom, celeste, apex]);
        Assert.Equal([doom, celeste, apex], await Shelf(statements, owner));
        Assert.Equal([elden], await Shelf(statements, other));

        // A book added afterwards follows the arranged ones, and a later partial order puts the rest after it.
        await statements.AddUserCategoryAsync(owner, elden);
        Assert.Equal([doom, celeste, apex, elden], await Shelf(statements, owner));
        await statements.SetShelfOrderAsync(owner, [elden, apex]);
        Assert.Equal([elden, apex, celeste, doom], await Shelf(statements, owner));

        // A book with clips stays; an empty one comes off, and doesn't come back with a stale order.
        Assert.Equal(RemoveShelfBookResult.HasClips, await statements.RemoveUserCategoryAsync(owner, celeste));
        Assert.Equal(RemoveShelfBookResult.Removed, await statements.RemoveUserCategoryAsync(owner, doom));
        Assert.Equal(RemoveShelfBookResult.NotOnShelf, await statements.RemoveUserCategoryAsync(owner, doom));
        await statements.SetShelfOrderAsync(owner, [doom, celeste, elden, apex]);
        Assert.Equal([celeste, elden, apex], await Shelf(statements, owner));

        // Someone else's shelf is untouched throughout.
        Assert.Equal([elden], await Shelf(statements, other));
    }

    private static async Task<List<Guid>> Shelf(GameCategoryStatements statements, Guid owner) =>
        (await statements.GetLibraryCategoriesAsync(owner)).Select(category => category.Id).ToList();

    private static async Task<Guid> AddCategory(NpgsqlConnection connection, string name)
    {
        Guid id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO game_category (id, name, slug) VALUES (@id, @name, @slug)",
            new { id, name, slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{id:N}" });
        return id;
    }

    private static Task InsertClip(NpgsqlConnection connection, Guid owner, Guid category) =>
        connection.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id)
            VALUES (@id, @owner, @id, @category)
            """, new { id = Guid.NewGuid(), owner, category });
}
