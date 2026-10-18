using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video.Migration;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The Bunny delete sweep's selection and deletes (<see cref="BunnyCopyStatements"/>) against a real Postgres, on the
/// minimal <c>clip</c> table, a <c>clip_collection</c> and the video pipeline's migrations in a schema of the test's own.
/// </summary>
public class BunnyCopyStatementsTests
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    [PostgresFact]
    public async Task GetExpiredCopies_TakesCopiesPastTheGracePeriodOldestMoveFirst()
    {
        await using TestSweep test = await TestSweep.CreateAsync();
        Guid older = await test.AddClipAsync(videoHost: "r2", bunnyVideoId: Guid.NewGuid(), migratedDaysAgo: 30);
        Guid newer = await test.AddClipAsync(videoHost: "r2", bunnyVideoId: Guid.NewGuid(), migratedDaysAgo: 8);
        Guid skipped = await test.AddClipAsync(videoHost: "r2", bunnyVideoId: Guid.NewGuid(), migratedDaysAgo: 9);
        // Not due: still in its grace period, already swept, or never moved.
        await test.AddClipAsync(videoHost: "r2", bunnyVideoId: Guid.NewGuid(), migratedDaysAgo: 6);
        await test.AddClipAsync(videoHost: "r2", bunnyVideoId: null, migratedDaysAgo: 30);
        await test.AddClipAsync(videoHost: "bunny");

        List<BunnyCopy> due = await test.Statements.GetExpiredCopiesAsync(KeepFor, [skipped], limit: 10);

        Assert.Equal([older, newer], due.Select(copy => copy.ClipId));
        Assert.Equal(await test.BunnyVideoIdAsync(older), due[0].BunnyVideoId);
        Assert.Equal([older], (await test.Statements.GetExpiredCopiesAsync(KeepFor, [], limit: 1)).Select(copy => copy.ClipId));
    }

    [PostgresFact]
    public async Task NeverSelectsOrDeletesABunnyVideoAClipStillPlaysFrom()
    {
        await using TestSweep test = await TestSweep.CreateAsync();
        Guid bunnyVideoId = Guid.NewGuid();
        Guid moved = await test.AddClipAsync(videoHost: "r2", bunnyVideoId: bunnyVideoId, migratedDaysAgo: 30);
        // Another clip plays from the same Bunny video.
        await test.AddClipAsync(videoHost: "bunny", videoId: bunnyVideoId);
        // A clip switched back to its Bunny copy without clearing bunny_video_id, its move left in place.
        Guid switchedBackVideo = Guid.NewGuid();
        Guid switchedBack = await test.AddClipAsync(videoHost: "bunny", videoId: switchedBackVideo,
            bunnyVideoId: switchedBackVideo, migratedDaysAgo: 30);

        Assert.Empty(await test.Statements.GetExpiredCopiesAsync(KeepFor, [], limit: 10));

        List<Guid> deletedAtHost = [];
        Assert.False(await test.Statements.DeleteCopyAsync(new BunnyCopy(moved, bunnyVideoId), KeepFor, Record(deletedAtHost)));
        Assert.False(await test.Statements.DeleteCopyAsync(new BunnyCopy(switchedBack, switchedBackVideo), KeepFor,
            Record(deletedAtHost)));
        Assert.Empty(deletedAtHost);
        Assert.Equal(bunnyVideoId, await test.BunnyVideoIdAsync(moved));
        Assert.Equal(switchedBackVideo, await test.BunnyVideoIdAsync(switchedBack));
    }

    [PostgresFact]
    public async Task DeleteCopy_DeletesAtTheHostAndClears_OrChangesNothingWhenTheHostFails()
    {
        await using TestSweep test = await TestSweep.CreateAsync();
        Guid bunnyVideoId = Guid.NewGuid();
        Guid clip = await test.AddClipAsync(videoHost: "r2", bunnyVideoId: bunnyVideoId, migratedDaysAgo: 30);
        BunnyCopy copy = new(clip, bunnyVideoId);

        await Assert.ThrowsAsync<HttpRequestException>(() => test.Statements.DeleteCopyAsync(copy, KeepFor,
            _ => throw new HttpRequestException("Bunny answered 500")));
        Assert.Equal(bunnyVideoId, await test.BunnyVideoIdAsync(clip));

        List<Guid> deletedAtHost = [];
        Assert.True(await test.Statements.DeleteCopyAsync(copy, KeepFor, Record(deletedAtHost)));
        Assert.Equal([bunnyVideoId], deletedAtHost);
        Assert.Null(await test.BunnyVideoIdAsync(clip));
        Assert.True(await test.Connection.ExecuteScalarAsync<bool>(
            "SELECT migrated_at IS NOT NULL FROM clip WHERE id = @clip", new { clip }));

        // Once swept, it's neither selected nor deleted again.
        Assert.Empty(await test.Statements.GetExpiredCopiesAsync(KeepFor, [], limit: 10));
        Assert.False(await test.Statements.DeleteCopyAsync(copy, KeepFor, Record(deletedAtHost)));
        Assert.Single(deletedAtHost);
    }

    [PostgresFact]
    public async Task Collections_AreDeletedOnceNoClipOnBunnyAndNoBunnyCopyRefersToThem()
    {
        await using TestSweep test = await TestSweep.CreateAsync();
        Guid owner = Guid.NewGuid();
        Guid otherOwner = Guid.NewGuid();
        Guid[] categories = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        // Still on Bunny: kept.
        await test.AddClipAsync(videoHost: "bunny", ownerId: owner, gameCategoryId: categories[0]);
        Guid onBunny = await test.AddCollectionAsync(owner, categories[0]);
        // Moved, its copy waiting out the grace period: kept.
        await test.AddClipAsync(videoHost: "r2", ownerId: owner, gameCategoryId: categories[1],
            bunnyVideoId: Guid.NewGuid(), migratedDaysAgo: 2);
        Guid waiting = await test.AddCollectionAsync(owner, categories[1]);
        // Moved and swept, or emptied: unused.
        await test.AddClipAsync(videoHost: "r2", ownerId: owner, gameCategoryId: categories[2], migratedDaysAgo: 30);
        Guid swept = await test.AddCollectionAsync(owner, categories[2]);
        Guid empty = await test.AddCollectionAsync(owner, categories[3]);
        // Another owner's clip in the same category doesn't keep this owner's collection.
        await test.AddClipAsync(videoHost: "bunny", ownerId: otherOwner, gameCategoryId: categories[2]);

        List<OwnedBunnyCollection> unused = await test.Statements.GetUnusedCollectionsAsync([], limit: 10);
        Assert.Equal(new[] { swept, empty }.Order(), unused.Select(collection => collection.CollectionId).Order());
        Assert.All(unused, collection => Assert.Equal(owner, collection.OwnerId));
        OwnedBunnyCollection sweptRow = unused.Single(collection => collection.CollectionId == swept);
        Assert.Equal([empty], (await test.Statements.GetUnusedCollectionsAsync([sweptRow.Id], limit: 10))
            .Select(collection => collection.CollectionId));

        // A failed delete at Bunny keeps the row; a successful one removes it.
        await Assert.ThrowsAsync<HttpRequestException>(() => test.Statements.DeleteCollectionAsync(sweptRow,
            _ => throw new HttpRequestException("Bunny answered 500")));
        Assert.True(await test.CollectionExistsAsync(swept));
        List<Guid> deletedAtHost = [];
        Assert.True(await test.Statements.DeleteCollectionAsync(sweptRow, Record(deletedAtHost)));
        Assert.Equal([swept], deletedAtHost);
        Assert.False(await test.CollectionExistsAsync(swept));

        // An upload reserved into the category after it was listed keeps it.
        OwnedBunnyCollection emptyRow = unused.Single(collection => collection.CollectionId == empty);
        await test.AddClipAsync(videoHost: "bunny", ownerId: owner, gameCategoryId: categories[3]);
        Assert.False(await test.Statements.DeleteCollectionAsync(emptyRow, Record(deletedAtHost)));
        Assert.True(await test.CollectionExistsAsync(empty));
        Assert.Single(deletedAtHost);
        Assert.True(await test.CollectionExistsAsync(onBunny));
        Assert.True(await test.CollectionExistsAsync(waiting));
    }

    private static Func<Guid, Task> Record(List<Guid> deleted) => id =>
    {
        deleted.Add(id);
        return Task.CompletedTask;
    };

    private sealed class TestSweep : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestSweep(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Statements = new BunnyCopyStatements(dataSource.CreateConnection());
        }

        /// <summary>A separate connection for arranging and checking rows.</summary>
        public NpgsqlConnection Connection { get; }

        public BunnyCopyStatements Statements { get; }

        public static async Task<TestSweep> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"bunny_sweep_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync("""
                CREATE TABLE clip_collection (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    owner_id uuid NOT NULL,
                    collection_id uuid NOT NULL,
                    game_category_id uuid NOT NULL,
                    UNIQUE (owner_id, game_category_id)
                )
                """);
            return new TestSweep(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(string videoHost, Guid? videoId = null, Guid? bunnyVideoId = null,
            int? migratedDaysAgo = null, Guid? ownerId = null, Guid? gameCategoryId = null)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host,
                                  bunny_video_id, migrated_at)
                VALUES (@ClipId, @OwnerId, @VideoId, @GameCategoryId, 'Clip', 'ready', @VideoHost, @BunnyVideoId,
                        now() - make_interval(days => @MigratedDaysAgo))
                """, new
            {
                ClipId = clipId,
                OwnerId = ownerId ?? Guid.NewGuid(),
                VideoId = videoId ?? (videoHost == "r2" ? clipId : Guid.NewGuid()),
                GameCategoryId = gameCategoryId ?? Guid.NewGuid(),
                VideoHost = videoHost,
                BunnyVideoId = bunnyVideoId,
                MigratedDaysAgo = migratedDaysAgo
            });
            return clipId;
        }

        public async Task<Guid> AddCollectionAsync(Guid ownerId, Guid gameCategoryId)
        {
            Guid collectionId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip_collection (owner_id, collection_id, game_category_id)
                VALUES (@OwnerId, @CollectionId, @GameCategoryId)
                """, new { OwnerId = ownerId, CollectionId = collectionId, GameCategoryId = gameCategoryId });
            return collectionId;
        }

        public Task<Guid?> BunnyVideoIdAsync(Guid clipId) => Connection.ExecuteScalarAsync<Guid?>(
            "SELECT bunny_video_id FROM clip WHERE id = @clipId", new { clipId });

        public Task<bool> CollectionExistsAsync(Guid collectionId) => Connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM clip_collection WHERE collection_id = @collectionId)", new { collectionId });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                await Connection.DisposeAsync();
                await _dataSource.DisposeAsync();
            }
        }
    }
}
