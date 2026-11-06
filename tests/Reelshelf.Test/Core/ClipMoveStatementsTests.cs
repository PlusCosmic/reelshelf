using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>
/// Moving clips between books against a real Postgres: only the owner's unbinned clips move, a filter moves every
/// match, a book never ends up holding the same file twice, and a moved clip remembers the book it was filed in. The
/// schema starts with the baseline's duplicate key and runs V61 over it, as prod will.
/// </summary>
public class ClipMoveStatementsTests
{
    private static readonly Guid ApexId = Guid.NewGuid();
    private static readonly Guid ValorantId = Guid.NewGuid();
    private static readonly Guid FortniteId = Guid.NewGuid();

    [PostgresFact]
    public async Task MovingIds_MovesTheOwnersUnbinnedClips_AndSaysWhereEachCameFrom()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid fromApex = await test.AddClipAsync(ApexId, "Apex", "a");
        Guid fromFortnite = await test.AddClipAsync(FortniteId, "Fortnite", "b");
        Guid alreadyThere = await test.AddClipAsync(ValorantId, "Valorant", "c");
        Guid binned = await test.AddClipAsync(ApexId, "Binned", "d");
        await test.Connection.ExecuteAsync("UPDATE clip SET binned_at = now() WHERE id = @binned", new { binned });
        Guid someoneElses = await test.AddClipAsync(ApexId, "Theirs", "e", owner: Guid.NewGuid());

        List<ClipsStatements.MoveClipRow> rows = await test.Clips.MoveClips(test.OwnerId, ValorantId,
            [fromApex, fromFortnite, alreadyThere, binned, someoneElses]);

        Assert.Equal(
            new[] { (fromApex, ApexId, true), (fromFortnite, FortniteId, true) }.OrderBy(row => row.Item1),
            rows.Select(row => (row.ClipId, row.FromCategoryId, row.Moved)).OrderBy(row => row.Item1));
        Assert.Equal(ValorantId, await test.CategoryOfAsync(fromApex));
        Assert.Equal(ValorantId, await test.CategoryOfAsync(fromFortnite));
        Assert.Equal(ApexId, await test.CategoryOfAsync(binned));
        Assert.Equal(ApexId, await test.CategoryOfAsync(someoneElses));
    }

    [PostgresFact]
    public async Task AFileTheBookAlreadyHolds_StaysWhereItIs()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.AddClipAsync(ValorantId, "Held", "same");
        Guid duplicate = await test.AddClipAsync(ApexId, "Duplicate", "same");
        Guid older = await test.AddClipAsync(ApexId, "Older", "twin", createdDaysAgo: 2);
        Guid twin = await test.AddClipAsync(FortniteId, "Twin", "twin", createdDaysAgo: 1);
        Guid fresh = await test.AddClipAsync(ApexId, "Fresh", "fresh");

        List<ClipsStatements.MoveClipRow> rows = await test.Clips.MoveClips(test.OwnerId, ValorantId,
            [duplicate, older, twin, fresh]);

        Assert.Equal(new[] { older, fresh }.Order(), rows.Where(row => row.Moved).Select(row => row.ClipId).Order());
        Assert.Equal(new[] { duplicate, twin }.Order(), rows.Where(row => !row.Moved).Select(row => row.ClipId).Order());
        Assert.Equal(ApexId, await test.CategoryOfAsync(duplicate));
        Assert.Equal(FortniteId, await test.CategoryOfAsync(twin));
    }

    [PostgresFact]
    public async Task ClipsWithoutAHash_ShareABook_AndAlwaysMove()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        // Twitch imports have no MD5; before V61 the second one in a book broke the duplicate key.
        await test.AddClipAsync(ValorantId, "Import 1", md5: null);
        await test.AddClipAsync(ValorantId, "Import 2", md5: null);
        Guid fromApex = await test.AddClipAsync(ApexId, "Import 3", md5: null);
        Guid fromFortnite = await test.AddClipAsync(FortniteId, "Import 4", md5: null);

        List<ClipsStatements.MoveClipRow> rows = await test.Clips.MoveClips(test.OwnerId, ValorantId,
            [fromApex, fromFortnite]);

        Assert.All(rows, row => Assert.True(row.Moved));
        Assert.Equal(4, await test.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM clip WHERE game_category_id = @ValorantId", new { ValorantId }));
    }

    [PostgresFact]
    public async Task TheDuplicateKey_StillRefusesTheSameFileTwiceInABook()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.AddClipAsync(ValorantId, "First", "same");

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => test.AddClipAsync(ValorantId, "Second", "same"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        // The baseline's key is gone, not just joined by the new one.
        Assert.Equal(0, await test.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_constraint WHERE conrelid = 'clip'::regclass AND contype = 'u'"));
    }

    [PostgresFact]
    public async Task ClipsMovedIntoApex_GetOneAutomaticLegendRun_OnceReady()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid ready = await test.AddClipAsync(ValorantId, "Ready", "a");
        Guid encoding = await test.AddClipAsync(ValorantId, "Encoding", "b");
        await test.Connection.ExecuteAsync("UPDATE clip SET video_state = 'processing' WHERE id = @encoding",
            new { encoding });
        Guid alreadyRun = await test.AddClipAsync(ValorantId, "Already run", "c");
        await test.Connection.ExecuteAsync("""
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model) VALUES (@alreadyRun, 'auto', 'openai', 'old')
            """, new { alreadyRun });
        Guid elsewhere = await test.AddClipAsync(FortniteId, "Not moved", "d");
        List<Guid> moved = (await test.Clips.MoveClips(test.OwnerId, ApexId, [ready, encoding, alreadyRun]))
            .Where(row => row.Moved).Select(row => row.ClipId).ToList();
        LegendDetectionStatements legend = new(test.NewConnection());

        Assert.Equal(1, await legend.QueueAutoRunsForClipsAsync([.. moved, elsewhere], ApexId, "openai", "new", null));
        Assert.Equal(0, await legend.QueueAutoRunsForClipsAsync(moved, ApexId, "openai", "new", null));
        Assert.Equal(
            new[] { (alreadyRun, "old"), (ready, "new") }.OrderBy(run => run.Item1),
            (await test.Connection.QueryAsync<(Guid, string)>("SELECT clip_id, model FROM legend_detection_run"))
                .OrderBy(run => run.Item1));
    }

    [PostgresFact]
    public async Task MovingAFilter_MovesEveryMatch_AndNothingElse()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        List<Guid> matches = [];
        for (int i = 0; i < 3; i++)
        {
            matches.Add(await test.AddClipAsync(ApexId, $"Valorant round {i}", $"m{i}"));
        }

        Guid other = await test.AddClipAsync(ApexId, "Apex win", "x");
        Guid otherBook = await test.AddClipAsync(FortniteId, "Valorant round elsewhere", "y");

        List<ClipsStatements.MoveClipRow> rows = await test.Clips.MoveClips(test.OwnerId, ValorantId, null,
            gameCategoryId: ApexId, search: "valorant");

        Assert.Equal(matches.Order(), rows.Select(row => row.ClipId).Order());
        Assert.All(rows, row => Assert.True(row.Moved));
        Assert.Equal(ApexId, await test.CategoryOfAsync(other));
        Assert.Equal(FortniteId, await test.CategoryOfAsync(otherBook));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestDatabase(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            _dataSource = dataSource;
            _schema = schema;
            Connection = connection;
            Clips = new ClipsStatements(dataSource.CreateConnection());
        }

        public NpgsqlConnection Connection { get; }

        public ClipsStatements Clips { get; }

        public Guid OwnerId { get; } = Guid.NewGuid();

        public NpgsqlConnection NewConnection() => _dataSource.CreateConnection();

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"clip_move_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync(
                "ALTER TABLE clip ADD UNIQUE NULLS NOT DISTINCT (owner_id, game_category_id, md5_hash)");
            await connection.ExecuteAsync(await File.ReadAllTextAsync(
                TestClipSchema.MigrationPath("V61__clip_md5_key_nulls_distinct.sql")));
            await connection.ExecuteAsync("""
                CREATE TABLE tag (id uuid PRIMARY KEY, name text NOT NULL);
                CREATE TABLE clip_tag (clip_id uuid NOT NULL, tag_id uuid NOT NULL);
                CREATE TABLE clip_view (clip_id uuid NOT NULL, user_id uuid NOT NULL);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                CREATE TABLE legend_detection_run (
                    clip_id uuid NOT NULL,
                    status text NOT NULL DEFAULT 'pending',
                    trigger text NOT NULL,
                    provider text NOT NULL,
                    model text NOT NULL,
                    reasoning_effort text,
                    archived_at timestamptz);
                CREATE UNIQUE INDEX ON legend_detection_run (clip_id) WHERE trigger = 'auto' AND archived_at IS NULL;
                INSERT INTO game_category (id, name, slug) VALUES (@ApexId, 'Apex Legends', 'apex-legends'),
                                                                   (@ValorantId, 'Valorant', 'valorant'),
                                                                   (@FortniteId, 'Fortnite', 'fortnite');
                """, new { ApexId, ValorantId, FortniteId });
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(Guid categoryId, string title, string? md5, Guid? owner = null,
            int createdDaysAgo = 0)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, md5_hash, video_state, created_at)
                VALUES (@ClipId, @OwnerId, @ClipId, @CategoryId, @Title, @Md5, 'ready',
                        now() - make_interval(days => @CreatedDaysAgo))
                """, new
            {
                ClipId = clipId,
                OwnerId = owner ?? OwnerId,
                CategoryId = categoryId,
                Title = title,
                Md5 = md5,
                CreatedDaysAgo = createdDaysAgo
            });
            return clipId;
        }

        public Task<Guid> CategoryOfAsync(Guid clipId) => Connection.ExecuteScalarAsync<Guid>(
            "SELECT game_category_id FROM clip WHERE id = @clipId", new { clipId });

        public async ValueTask DisposeAsync()
        {
            await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
        }
    }
}
