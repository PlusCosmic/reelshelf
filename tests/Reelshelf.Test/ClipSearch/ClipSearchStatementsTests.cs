using Dapper;
using Npgsql;
using Pgvector;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipSearch;
using Reelshelf.ClipSummary;
using Reelshelf.Core;
using Reelshelf.Test.ApexLegends;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.ClipSearch;

/// <summary>
/// The search's SQL against a real Postgres with pgvector, with fixture embeddings along known directions: filters
/// narrow the owner's clips by each clip's latest succeeded summary, and a phrase ranks them by cosine distance with a
/// cutoff.
/// </summary>
public class ClipSearchStatementsTests
{
    private const string EmbeddingModel = "text-embedding-3-small";
    private static readonly Guid CallOfDuty = Guid.NewGuid();
    private static readonly Guid Apex = Guid.NewGuid();

    private static readonly ClipSearchStatements.SearchFilters NoFilters = new(null, null, [], [], []);

    [PostgresFact]
    public async Task APhrase_RanksClosestFirst_AndLeavesOutFarClipsAndClipsWithoutASummary()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid exact = await test.AddClipAsync(CallOfDuty, "Exact", daysAgo: 30);
        await test.AddSummaryAsync(exact, Direction(0));
        Guid near = await test.AddClipAsync(CallOfDuty, "Near", daysAgo: 1);
        await test.AddSummaryAsync(near, Blend(0, 1));
        Guid far = await test.AddClipAsync(CallOfDuty, "Far");
        await test.AddSummaryAsync(far, Direction(1));
        await test.AddClipAsync(CallOfDuty, "No summary");
        Guid otherModel = await test.AddClipAsync(CallOfDuty, "Embedded by another model");
        await test.AddSummaryAsync(otherModel, Direction(0), embeddingModel: "text-embedding-4");
        Guid binned = await test.AddClipAsync(CallOfDuty, "Binned", binned: true);
        await test.AddSummaryAsync(binned, Direction(0));
        Guid someoneElses = await test.AddClipAsync(CallOfDuty, "Theirs", owner: Guid.NewGuid());
        await test.AddSummaryAsync(someoneElses, Direction(0));

        List<ClipSearchStatements.ClipSearchRow> rows = await test.Search.SearchAsync(test.OwnerId, NoFilters,
            Phrase(Direction(0)), 50);

        Assert.Equal([exact, near], rows.Select(row => row.Id));
        Assert.Equal(0, rows[0].Distance!.Value, precision: 5);
        Assert.Equal(1 - Math.Sqrt(0.5), rows[1].Distance!.Value, precision: 5);
        // Rows carry everything the library's projection needs.
        Assert.Equal("Exact", rows[0].Title);
        Assert.Equal("r2", rows[0].VideoHost);
        Assert.Equal("ready", rows[0].VideoState);
    }

    [PostgresFact]
    public async Task WithoutAPhrase_ClipsWithoutASummaryMatch_NewestFirst_WithinTheDates()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid old = await test.AddClipAsync(CallOfDuty, "Old", createdAt: Day(2025, 1, 1));
        Guid newer = await test.AddClipAsync(Apex, "Newer", createdAt: Day(2025, 12, 31).AddHours(23));
        await test.AddSummaryAsync(newer, Direction(3));
        await test.AddClipAsync(CallOfDuty, "Before", createdAt: Day(2024, 12, 31).AddHours(23));
        await test.AddClipAsync(CallOfDuty, "After", createdAt: Day(2026, 1, 1));
        await test.AddClipAsync(CallOfDuty, "Binned", createdAt: Day(2025, 6, 1), binned: true);

        List<ClipSearchStatements.ClipSearchRow> rows = await test.Search.SearchAsync(test.OwnerId,
            NoFilters with { From = Day(2025, 1, 1), Before = Day(2026, 1, 1) }, null, 50);

        Assert.Equal([newer, old], rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.Null(row.Distance));

        // The cap.
        Assert.Single(await test.Search.SearchAsync(test.OwnerId, NoFilters, null, 1));
    }

    [PostgresFact]
    public async Task Filters_NarrowByGame_TheLatestSummarysMoodTags_AndPeopleHeard()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid funnyWithSam = await test.AddClipAsync(CallOfDuty, "Funny with Sammy", daysAgo: 2);
        await test.AddSummaryAsync(funnyWithSam, Direction(0), moodTags: ["funny", "chaotic"], people: ["Sammy", "Jake"]);
        Guid funnyApex = await test.AddClipAsync(Apex, "Funny in Apex", daysAgo: 1);
        await test.AddSummaryAsync(funnyApex, Direction(0), moodTags: ["funny"], people: ["sam"]);
        Guid noLongerFunny = await test.AddClipAsync(CallOfDuty, "Was funny once");
        await test.AddSummaryAsync(noLongerFunny, Direction(0), moodTags: ["funny"], people: ["Sam"], completedDaysAgo: 3);
        await test.AddSummaryAsync(noLongerFunny, Direction(0), moodTags: ["chill"], people: [], completedDaysAgo: 2);
        // A failed run after the latest success changes nothing.
        await test.AddSummaryAsync(funnyWithSam, Direction(1), moodTags: [], people: [], status: "failed");
        Guid unsummarised = await test.AddClipAsync(CallOfDuty, "No summary");

        Assert.Equal([funnyApex, funnyWithSam],
            (await test.Search.SearchAsync(test.OwnerId, NoFilters with { MoodTags = ["funny"] }, null, 50))
            .Select(row => row.Id));
        Assert.Equal(new HashSet<Guid> { funnyWithSam, noLongerFunny, unsummarised },
            (await test.Search.SearchAsync(test.OwnerId, NoFilters with { GameCategoryIds = [CallOfDuty] }, null, 50))
            .Select(row => row.Id).ToHashSet());
        Assert.Equal([funnyWithSam],
            (await test.Search.SearchAsync(test.OwnerId,
                NoFilters with { GameCategoryIds = [CallOfDuty], MoodTags = ["funny"] }, null, 50))
            .Select(row => row.Id));
        // Part of a name, ignoring case, and every name asked for.
        Assert.Equal([funnyApex, funnyWithSam],
            (await test.Search.SearchAsync(test.OwnerId, NoFilters with { People = ["SAM"] }, null, 50))
            .Select(row => row.Id));
        Assert.Equal([funnyWithSam],
            (await test.Search.SearchAsync(test.OwnerId, NoFilters with { People = ["sam", "jake"] }, null, 50))
            .Select(row => row.Id));
        // Filters and a phrase together, ties newest first. The failed run's embedding is never compared.
        Assert.Equal([funnyApex, funnyWithSam],
            (await test.Search.SearchAsync(test.OwnerId, NoFilters with { MoodTags = ["funny"] },
                Phrase(Direction(0)), 50)).Select(row => row.Id));
        Assert.Empty(await test.Search.SearchAsync(test.OwnerId, NoFilters with { MoodTags = ["funny"] },
            Phrase(Direction(1)), 50));
    }

    [PostgresFact]
    public async Task ResultsCarryTheirTagsAndLegend()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid clip = await test.AddClipAsync(Apex, "Tagged");
        await test.Connection.ExecuteAsync("""
            INSERT INTO tag (id, name) VALUES (@TagId, 'clutch');
            INSERT INTO clip_tag (clip_id, tag_id) VALUES (@ClipId, @TagId);
            INSERT INTO legend_detection_run (clip_id, status, player_legend, completed_at)
            VALUES (@ClipId, 'succeeded', 'Wraith', now());
            """, new { ClipId = clip, TagId = Guid.NewGuid() });

        ClipSearchStatements.ClipSearchRow row = Assert.Single(
            await test.Search.SearchAsync(test.OwnerId, NoFilters, null, 50));
        Assert.Equal("clutch", row.TagNames);
        Assert.Equal("Wraith", ApexLegendNames.Canonicalize(row.PlayerLegend));
    }

    private static DateTimeOffset Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static ClipSearchStatements.SearchPhrase Phrase(float[] embedding) =>
        new(new Vector(embedding), EmbeddingModel, 0.8);

    /// <summary>A unit vector along one axis; two different axes are a cosine distance of 1 apart.</summary>
    private static float[] Direction(int axis)
    {
        float[] values = new float[ClipSummarizerFactory.EmbeddingDimensions];
        values[axis] = 1;
        return values;
    }

    /// <summary>Halfway between two axes, about 0.29 from each.</summary>
    private static float[] Blend(int first, int second)
    {
        float[] values = new float[ClipSummarizerFactory.EmbeddingDimensions];
        values[first] = MathF.Sqrt(0.5f);
        values[second] = MathF.Sqrt(0.5f);
        return values;
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
            Search = new ClipSearchStatements(dataSource.CreateConnection());
        }

        public NpgsqlConnection Connection { get; }

        public ClipSearchStatements Search { get; }

        public Guid OwnerId { get; } = Guid.NewGuid();

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"clip_search_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await PostgresFactAttribute.CreateExtensionsAsync(admin, "vector");
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            // The vector type lives in the schema the extension was created in.
            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema},public");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ReloadTypesAsync();
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync("""
                CREATE TABLE tag (id uuid PRIMARY KEY, name text NOT NULL);
                CREATE TABLE clip_tag (clip_id uuid NOT NULL, tag_id uuid NOT NULL);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                INSERT INTO game_category (id, name, slug) VALUES (@Apex, 'Apex Legends', 'apex-legends'),
                                                                   (@CallOfDuty, 'Call of Duty', 'call-of-duty');
                CREATE TABLE clip_transcription_run (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), clip_id uuid);
                """, new { Apex, CallOfDuty });
            await connection.ExecuteAsync(await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V35__clip_summary_run.sql")));
            await ClipLegendTests.CreateLegendTablesAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(
            Guid categoryId,
            string title,
            int daysAgo = 0,
            DateTimeOffset? createdAt = null,
            bool binned = false,
            Guid? owner = null)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, created_at, binned_at)
                VALUES (@ClipId, @OwnerId, @ClipId, @CategoryId, @Title, 'ready', @CreatedAt,
                        CASE WHEN @Binned THEN now() END)
                """, new
            {
                ClipId = clipId,
                OwnerId = owner ?? OwnerId,
                CategoryId = categoryId,
                Title = title,
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-daysAgo),
                Binned = binned
            });
            return clipId;
        }

        public async Task AddSummaryAsync(
            Guid clipId,
            float[] embedding,
            string[]? moodTags = null,
            string[]? people = null,
            string status = "succeeded",
            int completedDaysAgo = 0,
            string embeddingModel = EmbeddingModel)
        {
            Guid transcription = await Connection.QuerySingleAsync<Guid>(
                "INSERT INTO clip_transcription_run (clip_id) VALUES (@clipId) RETURNING id", new { clipId });
            await Connection.ExecuteAsync("""
                INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model, status,
                                              description, mood_tags, people, embedding, embedding_model, completed_at)
                VALUES (@ClipId, @Transcription, 'manual', 'openai', 'test', @Status, 'A clip', @MoodTags, @People,
                        @Embedding, @EmbeddingModel, now() - make_interval(days => @CompletedDaysAgo))
                """, new
            {
                ClipId = clipId,
                Transcription = transcription,
                Status = status,
                MoodTags = moodTags ?? [],
                People = people ?? [],
                Embedding = new Vector(embedding),
                EmbeddingModel = embeddingModel,
                CompletedDaysAgo = completedDaysAgo
            });
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
