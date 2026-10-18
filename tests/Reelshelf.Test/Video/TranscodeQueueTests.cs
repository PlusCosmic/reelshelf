using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;
using Reelshelf.Video;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Runs the transcode queue against a real Postgres, through <see cref="TranscodeQueue"/> as the worker endpoints
/// call it: the V40 migration on a minimal <c>clip</c> table in a schema of the test's own, dropped afterwards.
/// The statements share one closed connection, as they do in a request scope. Legend detection and transcription
/// are configured off, so the follow-ups queue nothing; their queueing is covered by their own tests.
/// </summary>
public class TranscodeQueueTests
{
    [PostgresFact]
    public async Task FakeWorker_ClaimsReportsProgressAndCompletes_AndTheClipEndsReady()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        Guid? runId = await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode,
            new TranscodeRunInput(SourceKey: $"uploads/{clipId}", TrimStartSeconds: 2.5));
        Assert.NotNull(runId);

        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((runId.Value, clipId, "transcode", 1, 1), (job.RunId, job.ClipId, job.Type, job.VideoVersion, job.Attempt));
        Assert.Equal($"uploads/{clipId}", job.Input.GetProperty("source_key").GetString());
        Assert.Equal(2.5, job.Input.GetProperty("trim_start_seconds").GetDouble());
        Assert.False(job.Input.TryGetProperty("trim_end_seconds", out _));
        Assert.Null(await test.Queue.ClaimAsync());

        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 40, true));
        Assert.Equal(("processing", 40), await test.ClipStateAsync(clipId));
        // A delayed report never takes the percentage back.
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 30, false));
        Assert.Equal(("processing", 40), await test.ClipStateAsync(clipId));
        Assert.True(await test.Connection.ExecuteScalarAsync<bool>(
            "SELECT thumbnails_ready_at IS NOT NULL FROM transcode_run WHERE id = @RunId", new { job.RunId }));

        using JsonDocument outputs = JsonDocument.Parse("""{"video": "clips/x/v1/av1.mp4", "thumbnail": "clips/x/v1/thumb.jpg"}""");
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 31.6, 12_345_678));

        ClipAfter clip = await test.Connection.QuerySingleAsync<ClipAfter>(
            "SELECT video_state, encode_progress, length, storage_size, video_status FROM clip WHERE id = @ClipId",
            new { ClipId = clipId });
        Assert.Equal(("ready", 100, 32, 12_345_678L, (int?)null),
            (clip.VideoState, clip.EncodeProgress, clip.Length, clip.StorageSize, clip.VideoStatus));

        (string status, string output) = await test.Connection.QuerySingleAsync<(string, string)>(
            "SELECT status, output::text FROM transcode_run WHERE id = @RunId", new { job.RunId });
        Assert.Equal("succeeded", status);
        using JsonDocument stored = JsonDocument.Parse(output);
        Assert.Equal("clips/x/v1/av1.mp4", stored.RootElement.GetProperty("outputs").GetProperty("video").GetString());
        Assert.Equal(12_345_678, stored.RootElement.GetProperty("size_bytes").GetInt64());

        // The worker retrying a completion whose response it lost is accepted; anything else on the job is not.
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 31.6, 12_345_678));
        Assert.Equal(TranscodeReportOutcome.LeaseLost,
            await test.Queue.CompleteAsync(job.RunId, Guid.NewGuid(), outputs.RootElement, 31.6, 12_345_678));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 50, false));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.FailAsync(job.RunId, job.LeaseId, "late", false));
        Assert.Equal(TranscodeReportOutcome.NotFound, await test.Queue.ReportProgressAsync(Guid.NewGuid(), job.LeaseId, 50, false));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task Enqueue_KeepsOneActiveTranscodePerClip()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");

        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a")));
        Assert.Null(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
        // Other job types are not limited.
        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("a")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            test.Queue.EnqueueAsync(clipId, "remux", new TranscodeRunInput()));

        // Once the running transcode ends, the clip can have another.
        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeRunTypes.Transcode, job.Type);
        Assert.Null(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
        await test.Queue.CompleteAsync(job.RunId, job.LeaseId, null, 10, 100);
        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
    }

    [PostgresFact]
    public async Task Claim_GoesByPriority_ThenAge()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid first = await test.AddClipAsync("ready");
        Guid second = await test.AddClipAsync("uploaded");
        Guid third = await test.AddClipAsync("uploaded");

        Guid? thumbnails = await test.Queue.EnqueueAsync(first, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("a"));
        Guid? older = await test.Queue.EnqueueAsync(second, TranscodeRunTypes.Transcode, new TranscodeRunInput("b"));
        Guid? newer = await test.Queue.EnqueueAsync(third, TranscodeRunTypes.Transcode, new TranscodeRunInput("c"));
        await test.Connection.ExecuteAsync("UPDATE transcode_run SET created_at = now() - interval '1 minute' WHERE id = @Id",
            new { Id = older });

        Assert.Equal(older, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Equal(newer, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Equal(thumbnails, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Null(await test.Queue.ClaimAsync());
    }

    [PostgresFact]
    public async Task RetryableFailure_WaitsTheRetryDelay_ThenIsClaimedUnderANewLease()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.FailAsync(first.RunId, first.LeaseId, "R2 timed out", true));
        // Repeating the report is harmless.
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.FailAsync(first.RunId, first.LeaseId, "R2 timed out", true));
        Assert.Equal("pending", await test.RunStatusAsync(first.RunId));
        Assert.NotEqual("encode_failed", (await test.ClipStateAsync(clipId)).State);
        Assert.Null(await test.Queue.ClaimAsync());

        await test.Connection.ExecuteAsync("UPDATE transcode_run SET available_at = now() - interval '1 second'");
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(2, second.Attempt);
        Assert.NotEqual(first.LeaseId, second.LeaseId);

        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.FailAsync(first.RunId, first.LeaseId, "again", true));
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.CompleteAsync(second.RunId, second.LeaseId, null, 5, 50));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task StaleJob_IsReclaimed_AndItsFirstWorkerCannotReportOverTheSecond()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        // A progress report is a heartbeat: a job quiet for a while that then reports is not reclaimed.
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 10, false));
        Assert.Null(await test.Queue.ClaimAsync());

        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((first.RunId, 2), (second.RunId, second.Attempt));

        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 90, false));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.CompleteAsync(first.RunId, first.LeaseId, null, 5, 50));
        Assert.Equal("processing", (await test.ClipStateAsync(clipId)).State);

        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.CompleteAsync(second.RunId, second.LeaseId, null, 7, 70));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task Transcode_ThatRunsOutOfAttempts_LeavesItsClipEncodeFailed()
    {
        await using TestQueue test = await TestQueue.CreateAsync(maxAttempts: 2);

        // Its worker goes quiet on the last attempt: the next claim gives up on it.
        Guid quiet = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(quiet, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Equal(2, (await test.Queue.ClaimAsync())?.Attempt);
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Null(await test.Queue.ClaimAsync());
        Assert.Equal("failed", await test.RunStatusAsync(job.RunId));
        Assert.StartsWith("Gave up after 2 attempts", await test.Connection.QuerySingleAsync<string>(
            "SELECT error FROM transcode_run WHERE id = @RunId", new { job.RunId }));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(quiet)).State);

        // A retryable failure on the last attempt fails the job there and then.
        Guid failing = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(failing, TranscodeRunTypes.Transcode, new TranscodeRunInput("b"));
        ClaimedTranscodeRun attempt = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.FailAsync(attempt.RunId, attempt.LeaseId, "worker ran out of disk", true);
        await test.Connection.ExecuteAsync("UPDATE transcode_run SET available_at = now() - interval '1 second'");
        attempt = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.FailAsync(attempt.RunId, attempt.LeaseId, "worker ran out of disk", true));
        Assert.Equal("failed", await test.RunStatusAsync(attempt.RunId));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(failing)).State);
    }

    [PostgresFact]
    public async Task NonRetryableFailure_FailsAtOnce_AndOnlyATranscodeFailsItsClip()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid uploaded = await test.AddClipAsync("uploaded");
        Guid ready = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(uploaded, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        await test.Queue.EnqueueAsync(ready, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("b"));

        ClaimedTranscodeRun transcode = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        ClaimedTranscodeRun thumbnails = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.FailAsync(transcode.RunId, transcode.LeaseId, new string('x', 10_000), false);
        await test.Queue.ReportProgressAsync(thumbnails.RunId, thumbnails.LeaseId, 50, true);
        await test.Queue.FailAsync(thumbnails.RunId, thumbnails.LeaseId, "no such output", false);

        Assert.Equal("failed", await test.RunStatusAsync(transcode.RunId));
        Assert.Equal(TranscodeQueue.MaxErrorLength, await test.Connection.QuerySingleAsync<int>(
            "SELECT length(error) FROM transcode_run WHERE id = @RunId", new { transcode.RunId }));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(uploaded)).State);
        Assert.Equal("failed", await test.RunStatusAsync(thumbnails.RunId));
        Assert.Equal("ready", (await test.ClipStateAsync(ready)).State);
        Assert.Null(await test.Queue.ClaimAsync());
    }

    private sealed class ClipAfter
    {
        public string VideoState { get; set; } = "";
        public int? EncodeProgress { get; set; }
        public int? Length { get; set; }
        public long? StorageSize { get; set; }
        public int? VideoStatus { get; set; }
    }

    private sealed class TestQueue : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestQueue(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema, TranscodeQueue queue)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Queue = queue;
        }

        /// <summary>A separate connection for arranging and checking rows.</summary>
        public NpgsqlConnection Connection { get; }

        public TranscodeQueue Queue { get; }

        public static async Task<TestQueue> CreateAsync(int maxAttempts = 3)
        {
            // The API sets this at startup; the statements' row classes rely on it.
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"transcode_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("""
                CREATE TABLE clip (
                    id uuid PRIMARY KEY,
                    owner_id uuid NOT NULL,
                    video_id uuid NOT NULL,
                    game_category_id uuid NOT NULL,
                    md5_hash text,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    title text,
                    length integer,
                    thumbnail_file_name text,
                    date_uploaded timestamptz,
                    storage_size bigint,
                    video_status integer,
                    video_state text NOT NULL DEFAULT 'awaiting_upload',
                    encode_progress integer,
                    file_size bigint
                )
                """);
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V40__transcode_run.sql")));

            // One closed connection shared by the statements, as the request scope shares one in the API.
            NpgsqlConnection scoped = dataSource.CreateConnection();
            ClipsStatements clips = new(scoped);
            ClipVideoUpdateHandler handler = new(
                null!,
                clips,
                new LegendDetectionService(null!, null!, null!, null!, null!,
                    new FixedOptions<LegendDetectionOptions>(new LegendDetectionOptions { AutoDetect = false }),
                    null!, NullLogger<LegendDetectionService>.Instance),
                new ClipTranscriptionService(null!, null!, null!, null!, null!, null!,
                    new FixedOptions<ClipTranscriptionOptions>(new ClipTranscriptionOptions { AutoQueue = false }),
                    null!, NullLogger<ClipTranscriptionService>.Instance),
                NullLogger<ClipVideoUpdateHandler>.Instance);
            TranscodeQueue queue = new(
                new TranscodeStatements(scoped),
                handler,
                new FixedOptions<TranscodingOptions>(new TranscodingOptions { MaxAttempts = maxAttempts }),
                NullLogger<TranscodeQueue>.Instance);
            return new TestQueue(dataSource, connection, schema, queue);
        }

        public async Task<Guid> AddClipAsync(string videoState)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state)
                VALUES (@ClipId, @OwnerId, @VideoId, @GameCategoryId, 'Clip', @VideoState)
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), VideoId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(),
                VideoState = videoState
            });
            return clipId;
        }

        public async Task<(string State, int? Progress)> ClipStateAsync(Guid clipId) =>
            await Connection.QuerySingleAsync<(string, int?)>(
                "SELECT video_state, encode_progress FROM clip WHERE id = @ClipId", new { ClipId = clipId });

        public async Task<string> RunStatusAsync(Guid runId) =>
            await Connection.QuerySingleAsync<string>("SELECT status FROM transcode_run WHERE id = @RunId", new { RunId = runId });

        public async Task BackdateHeartbeatAsync(TimeSpan by) =>
            await Connection.ExecuteAsync(
                "UPDATE transcode_run SET heartbeat_at = heartbeat_at - @By WHERE status = 'running'", new { By = by });

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

        private static string MigrationPath(string fileName)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "migrations", "db", "migrations", fileName);
        }
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
