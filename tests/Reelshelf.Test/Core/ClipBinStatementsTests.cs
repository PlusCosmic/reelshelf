using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Test.ApexLegends;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>
/// The bin (V55) against a real Postgres: binned clips leave the library's lists, totals, tags and legends but are
/// listed in the bin, filters bin every match, and the sweep finds what is due.
/// </summary>
public class ClipBinStatementsTests
{
    private static readonly Guid ApexId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    [PostgresFact]
    public async Task BinnedClips_LeaveTheLibrary_AndComeBackWhenRestored()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid kept = await test.AddClipAsync(ApexId, "Kept", tag: "clutch", legend: "Wraith");
        Guid binned = await test.AddClipAsync(ApexId, "Binned", tag: "whiff", legend: "Octane");
        Guid someoneElses = await test.AddClipAsync(OtherId, "Theirs", owner: Guid.NewGuid());

        Assert.Equal([binned], await test.Clips.BinClips(test.OwnerId, [binned, someoneElses]));
        // Binning again moves nothing.
        Assert.Empty(await test.Clips.BinClips(test.OwnerId, [binned]));

        ClipsStatements.PagedClipWithTagsRows library = await test.Clips.GetClipsWithTags(test.OwnerId, null);
        Assert.Equal([kept], library.Rows.Select(row => row.Id));
        Assert.Equal(1, library.TotalCount);
        Assert.Equal(1, (await test.Clips.GetCategoryTotalsByOwner(test.OwnerId)).Single().ClipCount);
        Assert.Equal(["clutch"], (await test.Clips.GetTagsOrderedByUsageForOwner(test.OwnerId)).Select(tag => tag.Name));
        Assert.Equal(["Wraith"], (await test.Clips.GetLegendCountsForOwner(test.OwnerId)).Select(row => row.Legend));

        ClipsStatements.PagedClipWithTagsRows bin = await test.Clips.GetClipsWithTags(test.OwnerId, null, binned: true);
        ClipsStatements.ClipWithTagsRow row = Assert.Single(bin.Rows);
        Assert.Equal(binned, row.Id);
        Assert.NotNull(row.BinnedAt);
        Assert.NotNull((await test.Clips.GetClipWithTagsByIdAndOwner(binned, test.OwnerId))!.BinnedAt);
        Assert.Equal(1, await test.Clips.CountBinnedClips(test.OwnerId));

        // The plan still counts it.
        Assert.Equal(2, (await test.Clips.GetOwnerClipUsage(test.OwnerId, DateTimeOffset.UtcNow.AddDays(-30))).ClipCount);

        Assert.Equal([binned], await test.Clips.RestoreClips(test.OwnerId, [binned, kept]));
        Assert.Equal(2, (await test.Clips.GetClipsWithTags(test.OwnerId, null)).TotalCount);
        Assert.Equal(0, await test.Clips.CountBinnedClips(test.OwnerId));
    }

    [PostgresFact]
    public async Task Binning_ClearsTheClipsPick()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid byId = await test.AddClipAsync(ApexId, "By id");
        Guid byFilter = await test.AddClipAsync(OtherId, "By filter");
        Guid untouched = await test.AddClipAsync(ApexId, "Untouched");
        await test.Connection.ExecuteAsync("UPDATE clip SET kept_at = now()");

        await test.Clips.BinClips(test.OwnerId, [byId]);
        await test.Clips.BinMatchingClips(test.OwnerId, OtherId, null, null, null, false);
        await test.Clips.RestoreClips(test.OwnerId, [byId, byFilter]);

        Dictionary<Guid, DateTimeOffset?> kept = (await test.Connection.QueryAsync<(Guid Id, DateTimeOffset? KeptAt)>(
            "SELECT id, kept_at FROM clip")).ToDictionary(row => row.Id, row => row.KeptAt);
        Assert.Null(kept[byId]);
        Assert.Null(kept[byFilter]);
        Assert.NotNull(kept[untouched]);
    }

    [PostgresFact]
    public async Task BinningAFilter_BinsEveryMatch_AndNothingElse()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        List<Guid> whiffs = [];
        for (int i = 0; i < 3; i++)
        {
            whiffs.Add(await test.AddClipAsync(ApexId, $"Whiff {i}", tag: "whiff"));
        }

        Guid otherGame = await test.AddClipAsync(OtherId, "Whiff elsewhere", tag: "whiff");
        Guid clutch = await test.AddClipAsync(ApexId, "Clutch", tag: "clutch");

        List<Guid> binned = await test.Clips.BinMatchingClips(test.OwnerId, ApexId, ["whiff"], null, null, false);
        Assert.Equal(whiffs.Order(), binned.Order());

        List<Guid> searched = await test.Clips.BinMatchingClips(test.OwnerId, null, null, "elsewhere", null, false);
        Assert.Equal([otherGame], searched);
        Assert.Equal([clutch], (await test.Clips.GetClipsWithTags(test.OwnerId, null)).Rows.Select(row => row.Id));
    }

    [PostgresFact]
    public async Task TheSweep_FindsClipsBinnedLongEnough_AndSkipsOnesTriedAlready()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid old = await test.AddClipAsync(ApexId, "Old");
        Guid older = await test.AddClipAsync(ApexId, "Older");
        Guid recent = await test.AddClipAsync(ApexId, "Recent");
        await test.Connection.ExecuteAsync("""
            UPDATE clip SET binned_at = now() - interval '40 days' WHERE id = @old;
            UPDATE clip SET binned_at = now() - interval '50 days' WHERE id = @older;
            UPDATE clip SET binned_at = now() - interval '1 day' WHERE id = @recent;
            """, new { old, older, recent });

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        Assert.Equal([older, old], (await test.Clips.GetClipsDueFromBin(cutoff, 10, [])).Select(clip => clip.Id));
        Assert.Equal([old], (await test.Clips.GetClipsDueFromBin(cutoff, 10, [older])).Select(clip => clip.Id));
        Assert.Equal([older], await test.Clips.GetBinnedClipIds(test.OwnerId, null, 1));
        Assert.Equal([recent], await test.Clips.GetBinnedClipIds(test.OwnerId, [recent, Guid.NewGuid()], 10));
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

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"clip_bin_test_{Guid.NewGuid():N}";
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
                CREATE TABLE tag (id uuid PRIMARY KEY, name text NOT NULL);
                CREATE TABLE clip_tag (clip_id uuid NOT NULL, tag_id uuid NOT NULL);
                CREATE TABLE clip_view (clip_id uuid NOT NULL, user_id uuid NOT NULL);
                CREATE TABLE clip_creation (owner_id uuid NOT NULL, clip_id uuid NOT NULL, created_at timestamptz NOT NULL);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                INSERT INTO game_category (id, name, slug) VALUES (@ApexId, 'Apex Legends', 'apex-legends'),
                                                                   (@OtherId, 'Valorant', 'valorant');
                """, new { ApexId, OtherId });
            await ClipLegendTests.CreateLegendTablesAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(Guid categoryId, string title, string? tag = null, string? legend = null,
            Guid? owner = null)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state)
                VALUES (@ClipId, @OwnerId, @ClipId, @CategoryId, @Title, 'ready')
                """, new { ClipId = clipId, OwnerId = owner ?? OwnerId, CategoryId = categoryId, Title = title });
            if (tag is not null)
            {
                await Connection.ExecuteAsync("""
                    INSERT INTO tag (id, name) SELECT gen_random_uuid(), @Tag WHERE NOT EXISTS (SELECT 1 FROM tag WHERE name = @Tag);
                    INSERT INTO clip_tag (clip_id, tag_id) SELECT @ClipId, id FROM tag WHERE name = @Tag;
                    """, new { ClipId = clipId, Tag = tag });
            }

            if (legend is not null)
            {
                await Connection.ExecuteAsync("""
                    INSERT INTO legend_detection_label (clip_id, player_legend) VALUES (@ClipId, @Legend)
                    """, new { ClipId = clipId, Legend = legend });
            }

            return clipId;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
        }
    }
}
