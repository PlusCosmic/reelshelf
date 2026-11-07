using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipSummary;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.QueueMetrics;

/// <summary>
/// The legend detection, transcription and summary queues write a result only under the claim that produced it
/// (#319): once a run is reclaimed after <c>StaleAfter</c>, the first processor's late complete or fail changes
/// nothing, and a fail after a success never moves the run back to pending. Runs against a real Postgres, on the
/// run tables as their migrations make them, in a schema of the test's own.
/// </summary>
public class RunClaimFenceTests
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 3;

    [PostgresFact]
    public async Task LegendDetection_LateResultsOfAReclaimedRun_ChangeNothing()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        LegendDetectionStatements statements = new(test.DataSource.CreateConnection());
        Guid clipId = await test.AddClipAsync();
        await statements.QueueAutoRunAsync(clipId, "openai", "model", null);

        LegendDetectionStatements.ClaimedRunRow first = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        Assert.Null(await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts));
        await test.AgeAsync("legend_detection_run", first.Id);
        LegendDetectionStatements.ClaimedRunRow second = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.ClaimId, second.ClaimId);
        Assert.Equal(2, second.Attempts);

        // The first processor's late answer and failure are both dropped while the reclaimer works.
        Assert.Equal((false, false), await statements.CompleteRunAsync(first.Id, first.ClaimId, "v1", 6, Recognition("Ash"), 10));
        Assert.False(await statements.FailRunAsync(first.Id, first.ClaimId, "boom", null, null, retry: true));
        Assert.Equal(("running", (string?)null), await test.StatusAsync("legend_detection_run", first.Id, "player_legend"));

        Assert.Equal((true, false), await statements.CompleteRunAsync(second.Id, second.ClaimId, "v1", 6, Recognition("Wraith"), 10));

        // Neither processor can change a finished run: no overwrite, and no retryable failure back to pending.
        Assert.Equal((false, false), await statements.CompleteRunAsync(first.Id, first.ClaimId, "v1", 6, Recognition("Ash"), 10));
        Assert.False(await statements.FailRunAsync(first.Id, first.ClaimId, "late", null, null, retry: true));
        Assert.False(await statements.FailRunAsync(second.Id, second.ClaimId, "late", null, null, retry: true));
        Assert.Equal(("succeeded", "Wraith"), await test.StatusAsync("legend_detection_run", first.Id, "player_legend"));
        Assert.Null(await statements.ClaimNextRunAsync(TimeSpan.Zero, TimeSpan.Zero, MaxAttempts));
    }

    [PostgresFact]
    public async Task LegendDetection_ArchivedRunsLateFailure_IsFailedNotPending()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        LegendDetectionStatements statements = new(test.DataSource.CreateConnection());
        Guid failingClip = await test.AddClipAsync();
        Guid answeringClip = await test.AddClipAsync();
        await statements.QueueAutoRunAsync(failingClip, "openai", "model", null);
        await statements.QueueAutoRunAsync(answeringClip, "openai", "model", null);
        LegendDetectionStatements.ClaimedRunRow failing = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        LegendDetectionStatements.ClaimedRunRow answering = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;

        Assert.Equal(2, await statements.ArchiveAllRunsAsync());

        Assert.True(await statements.FailRunAsync(failing.Id, failing.ClaimId, "boom", null, null, retry: true));
        Assert.Equal(("failed", (string?)"boom"), await test.StatusAsync("legend_detection_run", failing.Id, "error"));
        // An archived run's answer is kept, and reported as archived so it doesn't escalate.
        Assert.Equal((true, true), await statements.CompleteRunAsync(answering.Id, answering.ClaimId, "v1", 6, Recognition("Ash"), 10));
    }

    [PostgresFact]
    public async Task LegendDetection_ARunOutOfAttempts_IsFailedByTheClaimAndNeverClaimed()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        LegendDetectionStatements statements = new(test.DataSource.CreateConnection());
        await statements.QueueAutoRunAsync(await test.AddClipAsync(), "openai", "model", null);
        LegendDetectionStatements.ClaimedRunRow run = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, maxAttempts: 1))!;
        await test.AgeAsync("legend_detection_run", run.Id);

        Assert.Null(await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, maxAttempts: 1));
        Assert.Equal(("failed", (string?)"Gave up after 1 attempts"), await test.StatusAsync("legend_detection_run", run.Id, "error"));
        Assert.Equal(1, await test.Connection.ExecuteScalarAsync<int>(
            "SELECT attempts FROM legend_detection_run WHERE id = @Id", new { run.Id }));
        Assert.False(await statements.FailRunAsync(run.Id, run.ClaimId, "late", null, null, retry: true));
    }

    [PostgresFact]
    public async Task ClipTranscription_LateResultsOfAReclaimedRun_ChangeNothing()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        ClipTranscriptionStatements statements = new(test.DataSource.CreateConnection());
        Guid runId = await test.AddTranscriptionRunAsync(await test.AddClipAsync(), "pending");
        TranscriptionPrompt prompt = TranscriptionPrompt.For("Game", "game", "model");
        ClipAudio audio = new(true, null, 1000, 5);

        ClipTranscriptionStatements.ClaimedRunRow first = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        await test.AgeAsync("clip_transcription_run", runId);
        ClipTranscriptionStatements.ClaimedRunRow second = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        Assert.Equal((runId, runId), (first.Id, second.Id));
        Assert.NotEqual(first.ClaimId, second.ClaimId);

        Assert.False(await statements.CompleteRunAsync(runId, first.ClaimId, prompt, audio, Transcript("late"), null, 10));
        Assert.False(await statements.FailRunAsync(runId, first.ClaimId, "boom", prompt, retry: true));
        Assert.Equal(("running", (string?)null), await test.StatusAsync("clip_transcription_run", runId, "transcript"));

        Assert.True(await statements.CompleteRunAsync(runId, second.ClaimId, prompt, audio, Transcript("kept"), null, 10));
        Assert.False(await statements.CompleteRunAsync(runId, first.ClaimId, prompt, audio, Transcript("late"), null, 10));
        Assert.False(await statements.FailRunAsync(runId, first.ClaimId, "late", prompt, retry: true));
        Assert.False(await statements.FailRunAsync(runId, second.ClaimId, "late", prompt, retry: true));
        Assert.Equal(("succeeded", "kept"), await test.StatusAsync("clip_transcription_run", runId, "transcript"));
    }

    [PostgresFact]
    public async Task ClipSummary_LateResultsOfAReclaimedRun_ChangeNothing()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        ClipSummaryStatements statements = new(test.DataSource.CreateConnection());
        Guid clipId = await test.AddClipAsync();
        Guid sourceId = await test.AddTranscriptionRunAsync(clipId, "succeeded");
        Guid runId = await test.Connection.ExecuteScalarAsync<Guid>("""
            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model, embedding_model)
            VALUES (@ClipId, @SourceId, 'auto', 'openai', 'model', 'embedding') RETURNING id
            """, new { ClipId = clipId, SourceId = sourceId });
        ClipEmbedding embedding = new(new float[ClipSummarizerFactory.EmbeddingDimensions], 1);

        ClipSummaryStatements.ClaimedRunRow first = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        await test.AgeAsync("clip_summary_run", runId);
        ClipSummaryStatements.ClaimedRunRow second = (await statements.ClaimNextRunAsync(RetryAfter, StaleAfter, MaxAttempts))!;
        Assert.Equal((runId, runId), (first.Id, second.Id));
        Assert.NotEqual(first.ClaimId, second.ClaimId);

        Assert.False(await statements.CompleteRunAsync(runId, first.ClaimId, "v1", Summary("late"), embedding, 10));
        Assert.False(await statements.FailRunAsync(runId, first.ClaimId, "boom", null, null, retry: true));
        Assert.Equal(("running", (string?)null), await test.StatusAsync("clip_summary_run", runId, "description"));

        Assert.True(await statements.CompleteRunAsync(runId, second.ClaimId, "v1", Summary("kept"), embedding, 10));
        Assert.False(await statements.CompleteRunAsync(runId, first.ClaimId, "v1", Summary("late"), embedding, 10));
        Assert.False(await statements.FailRunAsync(runId, first.ClaimId, "late", null, null, retry: true));
        Assert.False(await statements.FailRunAsync(runId, second.ClaimId, "late", null, null, retry: true));
        Assert.Equal(("succeeded", "kept"), await test.StatusAsync("clip_summary_run", runId, "description"));
    }

    [PostgresFact]
    public async Task RunStatuses_AreChecked()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        Guid clipId = await test.AddClipAsync();
        Guid sourceId = await test.AddTranscriptionRunAsync(clipId, "succeeded");
        await test.Connection.ExecuteAsync("""
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model) VALUES (@ClipId, 'auto', 'openai', 'model');
            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model, embedding_model)
            VALUES (@ClipId, @SourceId, 'auto', 'openai', 'model', 'embedding');
            """, new { ClipId = clipId, SourceId = sourceId });
        foreach (string table in new[] { "legend_detection_run", "clip_transcription_run", "clip_summary_run" })
        {
            PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() =>
                test.Connection.ExecuteAsync($"UPDATE {table} SET status = 'cancelled'"));
            Assert.Equal($"{table}_status_check", refused.ConstraintName);
        }
    }

    private static LegendRecognition Recognition(string legend) =>
        new(LegendDetectionResult.Create(true, legend, 0.95), "{}", 1, 0, 1);

    private static ClipTranscript Transcript(string text) => new(text, ["en"], 1, 1, "{}");

    private static ClipSummarization Summary(string description) =>
        new(new ClipSummaryResult(description, [], [], []), "{}", 1, 0, 1);

    private sealed class TestSchema(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        : IAsyncDisposable
    {
        private static readonly string[] Migrations =
        [
            "V26__legend_detection_run.sql", "V27__legend_detection_cached_tokens.sql",
            "V28__legend_detection_archive.sql", "V30__legend_detection_reasoning_effort.sql",
            "V32__clip_transcription_run.sql", "V33__clip_transcription_fallback_model.sql",
            "V35__clip_summary_run.sql", "V37__legend_eval.sql", "V41__run_queue_metric_indexes.sql",
            "V57__run_claim_fence.sql", "V57__run_claim_fence.sql"
        ];

        private Guid _gameCategoryId;

        public NpgsqlDataSource DataSource => dataSource;
        public NpgsqlConnection Connection => connection;

        public static async Task<TestSchema> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"run_claim_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await PostgresFactAttribute.CreateExtensionsAsync(admin, "vector");
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            // public stays on the path for the vector type.
            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema},public");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            TestSchema test = new(dataSource, connection, schema);
            await connection.ExecuteAsync("""
                CREATE TABLE app_user (id uuid PRIMARY KEY);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                CREATE TABLE clip (
                    id uuid PRIMARY KEY, video_id uuid NOT NULL, video_host text NOT NULL DEFAULT 'r2',
                    video_version integer NOT NULL DEFAULT 1, video_codecs text, video_height integer,
                    video_state text NOT NULL DEFAULT 'ready', title text, created_at timestamptz NOT NULL DEFAULT now(),
                    game_category_id uuid NOT NULL REFERENCES game_category(id));
                CREATE TABLE transcode_run (clip_id uuid NOT NULL, video_version int NOT NULL, thumbnails_ready_at timestamptz);
                """);
            foreach (string migration in Migrations)
            {
                await connection.ExecuteAsync(await File.ReadAllTextAsync(TestClipSchema.MigrationPath(migration)));
            }

            test._gameCategoryId = await connection.ExecuteScalarAsync<Guid>(
                "INSERT INTO game_category (id, name, slug) VALUES (gen_random_uuid(), 'Game', 'game') RETURNING id");
            return test;
        }

        public async Task<Guid> AddClipAsync()
        {
            Guid clipId = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO clip (id, video_id, title, game_category_id) VALUES (@ClipId, gen_random_uuid(), 'Clip', @GameId)
                """, new { ClipId = clipId, GameId = _gameCategoryId });
            return clipId;
        }

        public async Task<Guid> AddTranscriptionRunAsync(Guid clipId, string status) =>
            await connection.ExecuteScalarAsync<Guid>("""
                INSERT INTO clip_transcription_run (clip_id, trigger, model, status, transcript, has_audio)
                VALUES (@ClipId, 'auto', 'model', @Status, CASE WHEN @Status = 'succeeded' THEN 'hello' END, true)
                RETURNING id
                """, new { ClipId = clipId, Status = status });

        /// <summary>Makes a running run look as if its processor went quiet an hour ago.</summary>
        public async Task AgeAsync(string table, Guid runId) =>
            await connection.ExecuteAsync($"UPDATE {table} SET started_at = now() - interval '1 hour' WHERE id = @RunId",
                new { RunId = runId });

        public async Task<(string Status, string? Value)> StatusAsync(string table, Guid runId, string column) =>
            await connection.QuerySingleAsync<(string, string?)>(
                $"SELECT status, {column}::text FROM {table} WHERE id = @RunId", new { RunId = runId });

        public async ValueTask DisposeAsync()
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
            await connection.DisposeAsync();
            await dataSource.DisposeAsync();
        }
    }
}
