using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Test.ApexLegends;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>
/// The owner's clip list (<see cref="ClipsStatements.GetClipsWithTags"/>), which cuts the page before it gathers each
/// clip's tags: paging and sort order over the whole match, every tag of a clip on the page (including tags the
/// search or filter didn't match), and the count of every match. Runs against a real Postgres in a schema of the
/// test's own, dropped afterwards.
/// </summary>
public class ClipPageQueryTests
{
    private static readonly Guid GameId = Guid.NewGuid();

    [PostgresFact]
    public async Task Pages_AreCutInSortOrder_AndCarryEveryTag()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid[] clips = new Guid[5];
        for (int i = 0; i < clips.Length; i++)
        {
            // Oldest first, an hour apart.
            clips[i] = await test.AddClipAsync($"Clip {i}", hoursAgo: clips.Length - i);
        }

        await test.TagAsync(clips[4], "clutch", "ace");
        await test.TagAsync(clips[2], "funny");

        ClipsStatements.PagedClipWithTagsRows first = await test.Clips.GetClipsWithTags(test.OwnerId, null, limit: 2);
        Assert.Equal(5, first.TotalCount);
        Assert.Equal([clips[4], clips[3]], first.Rows.Select(row => row.Id));
        Assert.Equal("ace,clutch", first.Rows[0].TagNames);
        Assert.Null(first.Rows[1].TagNames);

        ClipsStatements.PagedClipWithTagsRows second =
            await test.Clips.GetClipsWithTags(test.OwnerId, null, limit: 2, offset: 2);
        Assert.Equal([clips[2], clips[1]], second.Rows.Select(row => row.Id));
        Assert.Equal("funny", second.Rows[0].TagNames);

        ClipsStatements.PagedClipWithTagsRows last =
            await test.Clips.GetClipsWithTags(test.OwnerId, null, limit: 2, offset: 4);
        Assert.Equal([clips[0]], last.Rows.Select(row => row.Id));

        ClipsStatements.PagedClipWithTagsRows oldest = await test.Clips.GetClipsWithTags(
            test.OwnerId, null, sortOrder: ClipSortOrder.DateAscending, limit: 2);
        Assert.Equal([clips[0], clips[1]], oldest.Rows.Select(row => row.Id));
    }

    [PostgresFact]
    public async Task SearchAndTagFilters_KeepEveryTagOfAMatchingClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid tagged = await test.AddClipAsync("Ranked", hoursAgo: 2);
        await test.TagAsync(tagged, "clutch", "ace");
        Guid titled = await test.AddClipAsync("Clutch moment", hoursAgo: 1);
        await test.AddClipAsync("Nothing here", hoursAgo: 3);
        await test.AddClipAsync("Clutch, someone else's", hoursAgo: 1, ownerId: Guid.NewGuid());

        ClipsStatements.PagedClipWithTagsRows searched =
            await test.Clips.GetClipsWithTags(test.OwnerId, null, search: "clutch");
        Assert.Equal(2, searched.TotalCount);
        Assert.Equal([titled, tagged], searched.Rows.Select(row => row.Id));
        Assert.Equal("ace,clutch", searched.Rows[1].TagNames);

        ClipsStatements.PagedClipWithTagsRows filtered =
            await test.Clips.GetClipsWithTags(test.OwnerId, null, tags: ["ace"]);
        Assert.Equal(tagged, Assert.Single(filtered.Rows).Id);
        Assert.Equal("ace,clutch", filtered.Rows[0].TagNames);
        Assert.Equal(1, filtered.TotalCount);
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
            string schema = $"clip_page_test_{Guid.NewGuid():N}";
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
                CREATE TABLE tag (id uuid PRIMARY KEY, name text NOT NULL UNIQUE);
                CREATE TABLE clip_tag (clip_id uuid NOT NULL, tag_id uuid NOT NULL, PRIMARY KEY (clip_id, tag_id));
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                INSERT INTO game_category (id, name, slug) VALUES (@GameId, 'Valorant', 'valorant');
                """, new { GameId });
            await ClipLegendTests.CreateLegendTablesAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(string title, int hoursAgo, Guid? ownerId = null)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, created_at)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameId, @Title, 'ready', now() - make_interval(hours => @HoursAgo))
                """, new { ClipId = clipId, OwnerId = ownerId ?? OwnerId, GameId, Title = title, HoursAgo = hoursAgo });
            return clipId;
        }

        public async Task TagAsync(Guid clipId, params string[] names)
        {
            foreach (string name in names)
            {
                await Connection.ExecuteAsync("""
                    INSERT INTO tag (id, name) VALUES (gen_random_uuid(), @Name) ON CONFLICT (name) DO NOTHING;
                    INSERT INTO clip_tag (clip_id, tag_id) SELECT @ClipId, id FROM tag WHERE name = @Name;
                    """, new { ClipId = clipId, Name = name });
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
            await using NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
        }
    }
}
