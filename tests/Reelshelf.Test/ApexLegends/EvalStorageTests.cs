using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the eval queries against a real Postgres: creating an eval on the labelled clips, scoring and costing it,
/// listing its misses, and deleting it. Tables with the columns the queries touch are made in a schema of the
/// test's own, which is dropped afterwards. The statements get a closed connection, as they do in the API, where
/// Dapper opens it for each query.
/// </summary>
public class EvalStorageTests
{
    [PostgresFact]
    public async Task Eval_IsScoredAgainstLabels_CostedFromPrices_AndDeletedWithItsRuns()
    {
        // The API sets this at startup; the statements' row classes rely on it.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"eval_test_{Guid.NewGuid():N}";
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
            await RunAsync(connection, new LegendDetectionStatements(dataSource.CreateConnection()));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, LegendDetectionStatements statements)
    {
        await CreateTablesAsync(connection);

        Assert.Equal((null, 0), await statements.CreateEvalAsync("anthropic", "claude-haiku-5-5", "low", Guid.NewGuid()));

        Guid right = await AddLabelledClipAsync(connection, "Wraith");
        Guid wrong = await AddLabelledClipAsync(connection, null);
        Guid failed = await AddLabelledClipAsync(connection, "Ash");
        (Guid? evalId, int queued) = await statements.CreateEvalAsync("anthropic", "claude-haiku-5-5", "low", Guid.NewGuid());
        Assert.NotNull(evalId);
        Assert.Equal(3, queued);
        Assert.Equal(3, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM legend_detection_run WHERE trigger = 'eval' AND eval_id = @EvalId", new { EvalId = evalId }));

        await FinishAsync(connection, right, "succeeded", "Wraith", 0.9f, durationMs: 1000);
        await FinishAsync(connection, wrong, "succeeded", "Ash", 0.95f, durationMs: 3000);
        await FinishAsync(connection, failed, "failed", null, null, durationMs: null);

        LegendDetectionStatements.EvalRow unpriced = Assert.Single(await statements.GetEvalsAsync(0.8));
        Assert.Equal(3, unpriced.Scored);
        Assert.Equal(1, unpriced.Correct);
        Assert.Equal(1, unpriced.Failed);
        Assert.Equal(1, unpriced.ConfidentMistakes);
        Assert.Equal(2000, unpriced.P50DurationMs);
        Assert.Equal(2, unpriced.Unpriced);
        Assert.Null(LegendEval.From(unpriced).CostUsd);
        Assert.Equal(["v1"], unpriced.PromptVersions);
        Assert.Equal("claude-haiku-5-5", Assert.Single(await statements.GetPricesAsync()).Model);

        await statements.SetPriceAsync(new LegendDetectionStatements.PriceRow
        {
            Provider = "anthropic", Model = "claude-haiku-5-5", InputPerMtok = 2m, CachedInputPerMtok = 0.2m,
            OutputPerMtok = 10m
        });
        LegendEval priced = LegendEval.From(Assert.Single(await statements.GetEvalsAsync(0.8)));
        // Each succeeded run: 400 uncached at $2, 500 cached at $0.20, 100 cache writes at the input price and
        // 100 output at $10, per million tokens.
        Assert.Equal(2 * (400 * 2m + 500 * 0.2m + 100 * 2m + 100 * 10m) / 1_000_000, priced.CostUsd);
        Assert.False(priced.CostBilled);

        List<LegendDetectionStatements.EvalMissRow> misses = await statements.GetEvalMissesAsync(evalId!.Value);
        Assert.Equal([wrong, failed], misses.Select(miss => miss.ClipId));
        Assert.Equal("Ash", misses[0].PlayerLegend);
        Assert.Null(misses[0].LabelLegend);
        Assert.All(misses, miss => Assert.Equal("bunny", miss.VideoHost));

        // A second eval answers two of the three clips; the third is still queued, so the cascade leaves it out.
        (Guid? escalationId, _) = await statements.CreateEvalAsync("anthropic", "claude-sonnet-5-5", "low", Guid.NewGuid());
        await connection.ExecuteAsync("""
            UPDATE legend_detection_run
            SET status = 'succeeded', duration_ms = 2500,
                player_legend = CASE WHEN clip_id = @Right THEN 'Wraith' END
            WHERE eval_id = @EvalId AND clip_id IN (@Right, @Wrong)
            """, new { EvalId = escalationId, Right = right, Wrong = wrong });
        List<LegendDetectionStatements.CascadeRow> cascade =
            (await statements.GetCascadeRowsAsync(evalId.Value, escalationId!.Value)).OrderBy(row => row.FirstDurationMs).ToList();
        Assert.Equal([right, wrong], cascade.Select(row => row.ClipId));
        Assert.Equal(("Wraith", "Wraith", 0.9f), (cascade[0].LabelLegend, cascade[0].FirstLegend, cascade[0].FirstConfidence));
        Assert.Equal(("Ash", (string?)null, 2500), (cascade[1].FirstLegend, cascade[1].EscalationLegend, cascade[1].EscalationDurationMs));
        Assert.Equal(priced.CostUsd / 2, cascade[0].FirstCostUsd);
        Assert.Null(cascade[0].EscalationCostUsd);

        Assert.True(await statements.DeleteEvalAsync(escalationId.Value));
        Assert.True(await statements.DeleteEvalAsync(evalId.Value));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM legend_detection_run"));
        Assert.False(await statements.EvalExistsAsync(evalId.Value));
    }

    private static async Task<Guid> AddLabelledClipAsync(NpgsqlConnection connection, string? legend)
    {
        Guid clipId = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO clip (id, video_id, title) VALUES (@ClipId, @VideoId, 'Clip')",
            new { ClipId = clipId, VideoId = Guid.NewGuid() });
        await connection.ExecuteAsync(
            "INSERT INTO legend_detection_label (clip_id, player_legend) VALUES (@ClipId, @Legend)",
            new { ClipId = clipId, Legend = legend });
        return clipId;
    }

    private static async Task FinishAsync(
        NpgsqlConnection connection,
        Guid clipId,
        string status,
        string? legend,
        float? confidence,
        int? durationMs)
    {
        await connection.ExecuteAsync("""
            UPDATE legend_detection_run
            SET status = @Status, player_legend = @Legend, player_legend_confidence = @Confidence,
                prompt_version = 'v1', duration_ms = @DurationMs, started_at = now(), completed_at = now(),
                input_tokens = CASE WHEN @Status = 'succeeded' THEN 1000 END,
                cached_input_tokens = CASE WHEN @Status = 'succeeded' THEN 500 END,
                cache_write_input_tokens = CASE WHEN @Status = 'succeeded' THEN 100 END,
                output_tokens = CASE WHEN @Status = 'succeeded' THEN 100 END
            WHERE clip_id = @ClipId
            """, new { ClipId = clipId, Status = status, Legend = legend, Confidence = confidence, DurationMs = durationMs });
    }

    private static async Task CreateTablesAsync(NpgsqlConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE clip (id uuid PRIMARY KEY, video_id uuid NOT NULL, video_host text NOT NULL DEFAULT 'bunny', title text);
            CREATE TABLE legend_detection_label (
                clip_id uuid PRIMARY KEY REFERENCES clip(id) ON DELETE CASCADE,
                player_legend text,
                teammate_legends text[] NOT NULL DEFAULT '{}',
                labelled_by uuid,
                labelled_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE legend_eval (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                provider text NOT NULL,
                model text NOT NULL,
                reasoning_effort text,
                created_by uuid,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE legend_model_price (
                provider text NOT NULL,
                model text NOT NULL,
                input_per_mtok numeric(12, 6),
                cached_input_per_mtok numeric(12, 6),
                cache_write_per_mtok numeric(12, 6),
                output_per_mtok numeric(12, 6),
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (provider, model)
            );
            CREATE TABLE legend_detection_run (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
                trigger text NOT NULL,
                provider text NOT NULL,
                model text NOT NULL,
                reasoning_effort text,
                prompt_version text,
                status text NOT NULL DEFAULT 'pending',
                hud_detected boolean,
                player_legend text,
                player_legend_confidence real,
                input_tokens int,
                cached_input_tokens int,
                cache_write_input_tokens int,
                output_tokens int,
                cost_usd numeric(14, 8),
                duration_ms int,
                error text,
                eval_id uuid REFERENCES legend_eval(id) ON DELETE CASCADE,
                archived_at timestamptz,
                created_at timestamptz NOT NULL DEFAULT now(),
                started_at timestamptz,
                completed_at timestamptz
            );
            """);
    }
}
