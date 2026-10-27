using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Games;
using Reelshelf.Test.Core;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// The legend a clip carries to the clip lists and its page: an Apex clip's label, else its latest succeeded run
/// outside evals and archives, and nothing for another game. Runs the clip statements against a real Postgres in a
/// schema of the test's own, dropped afterwards; the portrait check needs no database.
/// </summary>
public class ClipLegendTests
{
    private static readonly GameCategory Apex = new() { Id = Guid.NewGuid(), Name = "Apex Legends", Slug = "apex-legends" };
    private static readonly GameCategory Other = new() { Id = Guid.NewGuid(), Name = "Valorant", Slug = "valorant" };

    /// <summary>The columns of <c>legend_detection_label</c> and <c>legend_detection_run</c> the clip queries read.</summary>
    internal static async Task CreateLegendTablesAsync(NpgsqlConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE legend_detection_label (clip_id uuid PRIMARY KEY, player_legend text);
            CREATE TABLE legend_detection_run (
                clip_id uuid NOT NULL,
                status text NOT NULL,
                trigger text NOT NULL DEFAULT 'auto',
                player_legend text,
                archived_at timestamptz,
                completed_at timestamptz
            );
            """);
    }

    [Fact]
    public void EveryLegend_HasAPortrait()
    {
        string migrations = Path.GetDirectoryName(TestClipSchema.MigrationPath("x"))!;
        string root = Path.GetFullPath(Path.Combine(migrations, "..", "..", ".."));
        foreach (string legend in ApexLegendNames.All)
        {
            // The frontend's legendPortraitUrl: lower case, each run of other characters one hyphen.
            string slug = System.Text.RegularExpressions.Regex.Replace(legend.ToLowerInvariant(), "[^a-z]+", "-")
                .Trim('-');
            Assert.True(File.Exists(Path.Combine(root, "frontend", "public", "apex-legends", $"{slug}.png")),
                $"No portrait for {legend} at frontend/public/apex-legends/{slug}.png");
        }
    }

    [PostgresFact]
    public async Task AnApexClip_CarriesItsLatestSucceededRun()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid clipId = await test.AddClipAsync(Apex);
        await test.AddRunAsync(clipId, "succeeded", "Bloodhound", completedMinutesAgo: 30);
        await test.AddRunAsync(clipId, "succeeded", "Wraith", completedMinutesAgo: 20);
        await test.AddRunAsync(clipId, "failed", "Octane", completedMinutesAgo: 10);
        await test.AddRunAsync(clipId, "succeeded", "Lifeline", completedMinutesAgo: 5, trigger: "eval");
        await test.AddRunAsync(clipId, "succeeded", "Horizon", completedMinutesAgo: 1, archived: true);

        Assert.Equal("Wraith", await test.LegendAsync(clipId));
        Assert.Equal("Wraith", await test.ListedLegendAsync(clipId));
    }

    [PostgresFact]
    public async Task ALabel_WinsOverRuns_EvenWhenItSaysTheLegendCantBeTold()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid labelled = await test.AddClipAsync(Apex);
        Guid cantTell = await test.AddClipAsync(Apex);
        foreach (Guid clipId in new[] { labelled, cantTell })
        {
            await test.AddRunAsync(clipId, "succeeded", "Wraith", completedMinutesAgo: 1);
        }

        await test.Connection.ExecuteAsync("""
            INSERT INTO legend_detection_label (clip_id, player_legend)
            VALUES (@Labelled, 'Mad Maggie'), (@CantTell, NULL)
            """, new { Labelled = labelled, CantTell = cantTell });

        Assert.Equal("Mad Maggie", await test.LegendAsync(labelled));
        Assert.Null(await test.LegendAsync(cantTell));
    }

    [PostgresFact]
    public async Task OtherGames_AndUnknownLegends_CarryNone()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid otherGame = await test.AddClipAsync(Other);
        await test.AddRunAsync(otherGame, "succeeded", "Wraith", completedMinutesAgo: 1);
        Guid notOnTheSheet = await test.AddClipAsync(Apex);
        await test.AddRunAsync(notOnTheSheet, "succeeded", "Someone New", completedMinutesAgo: 1);
        Guid undetected = await test.AddClipAsync(Apex);

        Assert.Null(await test.LegendAsync(otherGame));
        Assert.Null(await test.LegendAsync(notOnTheSheet));
        Assert.Null(await test.LegendAsync(undetected));
    }

    [PostgresFact]
    public async Task TheLegendFilter_AndCounts_UseTheSameLegendAsTheClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid wraith = await test.AddClipAsync(Apex);
        await test.AddRunAsync(wraith, "succeeded", "Wraith", completedMinutesAgo: 1);
        Guid relabelled = await test.AddClipAsync(Apex);
        await test.AddRunAsync(relabelled, "succeeded", "Octane", completedMinutesAgo: 1);
        await test.Connection.ExecuteAsync(
            "INSERT INTO legend_detection_label (clip_id, player_legend) VALUES (@ClipId, 'Wraith')",
            new { ClipId = relabelled });
        Guid octane = await test.AddClipAsync(Apex);
        await test.AddRunAsync(octane, "succeeded", "Octane", completedMinutesAgo: 1);
        Guid otherGame = await test.AddClipAsync(Other);
        await test.AddRunAsync(otherGame, "succeeded", "Wraith", completedMinutesAgo: 1);
        await test.AddClipAsync(Apex);

        ClipsStatements.PagedClipWithTagsRows page =
            await test.Clips.GetClipsWithTags(test.OwnerId, null, limit: 1, legend: "Wraith");
        Assert.Equal(2, page.TotalCount);
        Assert.Contains(Assert.Single(page.Rows).Id, new[] { wraith, relabelled });

        List<ClipsStatements.LegendCountRow> counts = await test.Clips.GetLegendCountsForOwner(test.OwnerId);
        Assert.Equal(
            [("Wraith", 2), ("Octane", 1)],
            counts.Select(row => (row.Legend, row.Count)));
        Assert.Empty(await test.Clips.GetLegendCountsForOwner(Guid.NewGuid()));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;
        private readonly ClipProjection _projection =
            new(ClipMediaUrlsTests.Create("https://cdn.example", "https://media.example"));

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
            string schema = $"clip_legend_test_{Guid.NewGuid():N}";
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
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                INSERT INTO game_category (id, name, slug) VALUES (@ApexId, 'Apex Legends', 'apex-legends'),
                                                                   (@OtherId, 'Valorant', 'valorant');
                """, new { ApexId = Apex.Id, OtherId = Other.Id });
            await CreateLegendTablesAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(GameCategory category)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', 'ready')
                """, new { ClipId = clipId, OwnerId, GameCategoryId = category.Id });
            return clipId;
        }

        public async Task AddRunAsync(
            Guid clipId,
            string status,
            string legend,
            int completedMinutesAgo,
            string trigger = "auto",
            bool archived = false) =>
            await Connection.ExecuteAsync("""
                INSERT INTO legend_detection_run (clip_id, status, trigger, player_legend, archived_at, completed_at)
                VALUES (@ClipId, @Status, @Trigger, @Legend, CASE WHEN @Archived THEN now() END,
                        now() - make_interval(mins => @CompletedMinutesAgo))
                """, new
            {
                ClipId = clipId,
                Status = status,
                Trigger = trigger,
                Legend = legend,
                Archived = archived,
                CompletedMinutesAgo = completedMinutesAgo
            });

        /// <summary>The legend on the clip's own page.</summary>
        public async Task<string?> LegendAsync(Guid clipId)
        {
            ClipsStatements.ClipWithTagsRow row = Assert.IsType<ClipsStatements.ClipWithTagsRow>(
                await Clips.GetClipWithTagsById(clipId));
            return Project(row).Legend;
        }

        /// <summary>The legend on the clip's row in the owner's list.</summary>
        public async Task<string?> ListedLegendAsync(Guid clipId)
        {
            ClipsStatements.ClipWithTagsRow row = (await Clips.GetClipsWithTags(OwnerId, null)).Rows
                .Single(row => row.Id == clipId);
            return Project(row).Legend;
        }

        private Clip Project(ClipsStatements.ClipWithTagsRow row) =>
            _projection.ProjectClip(row, row.GameCategoryId == Apex.Id ? Apex : Other, false, false);

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
