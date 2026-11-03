using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>The uploader's batch duplicate check, against a real Postgres.</summary>
public class DuplicateMd5StatementsTests
{
    [PostgresFact]
    public async Task FindExistingMd5Hashes_KeepsTheOwnersHashesInTheCategory()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"md5_test_{Guid.NewGuid():N}";
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
            await connection.ExecuteAsync("CREATE TABLE app_user (id uuid PRIMARY KEY)");
            await TestClipSchema.CreateAsync(connection);
            ClipsStatements statements = new(dataSource.CreateConnection());

            Guid owner = Guid.NewGuid();
            Guid other = Guid.NewGuid();
            Guid book = Guid.NewGuid();
            Guid otherBook = Guid.NewGuid();
            await connection.ExecuteAsync("INSERT INTO app_user (id) VALUES (@owner), (@other)", new { owner, other });
            await InsertClip(connection, owner, book, "aaa");
            await InsertClip(connection, owner, book, "aaa");
            await InsertClip(connection, owner, otherBook, "bbb");
            await InsertClip(connection, other, book, "ccc");

            HashSet<string> found = await statements.FindExistingMd5Hashes(owner, book, ["aaa", "bbb", "ccc", "ddd"]);

            Assert.Equal(["aaa"], found);
            Assert.Empty(await statements.FindExistingMd5Hashes(owner, book, []));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static Task InsertClip(NpgsqlConnection connection, Guid owner, Guid category, string md5) =>
        connection.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, md5_hash)
            VALUES (@id, @owner, @id, @category, @md5)
            """, new { id = Guid.NewGuid(), owner, category, md5 });
}
