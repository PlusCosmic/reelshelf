using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Plans;

/// <summary>Runs the plan's clip counts against a real Postgres, on the V46 migration itself.</summary>
public class ClipUsageStatementsTests
{
    [PostgresFact]
    public async Task ClipsAndCreations_AreCounted_AndOldCreationsForgotten()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"plan_test_{Guid.NewGuid():N}";
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
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V46__clip_creation.sql")));
            await RunAsync(connection, new ClipsStatements(dataSource.CreateConnection()));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, ClipsStatements statements)
    {
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO app_user (id) VALUES (@owner), (@other)", new { owner, other });
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Two clips kept (one still reserved with its declared size, one encoded), and one of someone else's.
        await InsertClip(connection, owner, fileSize: 1_000, storageSize: null);
        await InsertClip(connection, owner, fileSize: null, storageSize: 5_000);
        await InsertClip(connection, other, fileSize: 7, storageSize: null);

        // Three creations: one long ago, one recent whose clip was deleted since, one kept.
        await connection.ExecuteAsync(
            "INSERT INTO clip_creation (owner_id, clip_id, created_at) VALUES (@owner, @clip, @at)",
            new { owner, clip = Guid.NewGuid(), at = now.AddDays(-40) });
        await statements.RecordClipCreation(owner, Guid.NewGuid(), forgetBefore: now.AddDays(-50));
        Guid kept = Guid.NewGuid();
        await statements.RecordClipCreation(owner, kept, forgetBefore: now.AddDays(-50));

        ClipsStatements.OwnerClipUsage usage = await statements.GetOwnerClipUsage(owner, now.AddDays(-30));
        Assert.Equal(new ClipsStatements.OwnerClipUsage(2, 2, 6_000), usage);

        // Recording a creation forgets the owner's ones older than the window, and only theirs.
        await statements.RecordClipCreation(other, Guid.NewGuid(), forgetBefore: now.AddDays(-30));
        Assert.Equal(3, await CountCreations(connection, owner));
        await statements.RecordClipCreation(owner, Guid.NewGuid(), forgetBefore: now.AddDays(-30));
        Assert.Equal(3, await CountCreations(connection, owner));

        // A reservation given back forgets its own creation.
        await statements.ForgetClipCreation(kept);
        Assert.Equal(2, await CountCreations(connection, owner));
        Assert.Equal(1, (await statements.GetOwnerClipUsage(other, now.AddDays(-30))).NewClips);
    }

    private static Task InsertClip(NpgsqlConnection connection, Guid owner, long? fileSize, long? storageSize) =>
        connection.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, file_size, storage_size)
            VALUES (@id, @owner, @id, @id, @fileSize, @storageSize)
            """, new { id = Guid.NewGuid(), owner, fileSize, storageSize });

    private static Task<int> CountCreations(NpgsqlConnection connection, Guid owner) =>
        connection.QuerySingleAsync<int>("SELECT COUNT(*)::int FROM clip_creation WHERE owner_id = @owner", new { owner });
}
