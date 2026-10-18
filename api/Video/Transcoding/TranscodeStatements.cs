using System.Data;
using Dapper;
using Npgsql;

namespace Reelshelf.Video.Transcoding;

/// <summary>
/// The <c>transcode_run</c> queue. Every report from a worker is conditional on the job still running under the
/// lease the worker was given when it claimed it, so a worker whose job was reclaimed cannot report over the one
/// that holds it now.
/// </summary>
public class TranscodeStatements(NpgsqlConnection connection)
{
    /// <summary>
    /// Queues a job. Returns its id, or null when it is a <see cref="TranscodeRunTypes.Transcode"/> and the clip
    /// already has one pending or running.
    /// </summary>
    public async Task<Guid?> EnqueueAsync(Guid clipId, string type, string inputJson, int videoVersion, int priority)
    {
        const string sql = """
            INSERT INTO transcode_run (clip_id, type, input, video_version, priority)
            VALUES (@ClipId, @Type, @Input::jsonb, @VideoVersion, @Priority)
            ON CONFLICT (clip_id) WHERE type = 'transcode' AND status IN ('pending', 'running') DO NOTHING
            RETURNING id
            """;
        return await connection.QuerySingleOrDefaultAsync<Guid?>(sql, new
        {
            ClipId = clipId, Type = type, Input = inputJson, VideoVersion = videoVersion, Priority = priority
        });
    }

    /// <summary>
    /// Fails the jobs that have used up their attempts: running ones whose worker went quiet past
    /// <paramref name="staleAfter"/> on their last attempt, and pending ones left over when the cap was lowered.
    /// SKIP LOCKED leaves rows another claim is already failing (or a worker is reporting on) to that one.
    /// </summary>
    public async Task<List<FinishedRunRow>> FailExhaustedRunsAsync(int maxAttempts, TimeSpan staleAfter)
    {
        const string sql = """
            UPDATE transcode_run
            SET status = 'failed',
                finished_at = now(),
                error = 'Gave up after ' || attempts || ' attempts' || COALESCE(': ' || error, '')
            WHERE id IN (
                SELECT id FROM transcode_run
                WHERE attempts >= @MaxAttempts
                  AND (status = 'pending' OR (status = 'running' AND heartbeat_at < now() - @StaleAfter))
                FOR UPDATE SKIP LOCKED)
            RETURNING id, clip_id, type, status, video_version
            """;
        return (await connection.QueryAsync<FinishedRunRow>(sql, new { MaxAttempts = maxAttempts, StaleAfter = staleAfter }))
            .ToList();
    }

    /// <summary>
    /// Claims the next job for a worker and counts the attempt: a pending one whose retry delay has passed, or a
    /// running one whose worker has not reported for <paramref name="staleAfter"/>, lowest priority first and then
    /// the oldest. Each claim issues a new lease, and the run comes back with its clip's current video version, which
    /// a trim reads from. SKIP LOCKED keeps two workers (or API instances) from claiming
    /// the same job; a row another claim changed meanwhile is re-checked against the conditions and skipped.
    /// </summary>
    public async Task<ClaimedRunRow?> ClaimNextRunAsync(int maxAttempts, TimeSpan staleAfter)
    {
        const string sql = """
            UPDATE transcode_run run
            SET status = 'running',
                attempts = run.attempts + 1,
                started_at = now(),
                heartbeat_at = now(),
                lease_id = gen_random_uuid(),
                progress = NULL
            WHERE run.id = (
                SELECT id FROM transcode_run
                WHERE attempts < @MaxAttempts
                  AND ((status = 'pending' AND available_at <= now())
                    OR (status = 'running' AND heartbeat_at < now() - @StaleAfter))
                ORDER BY priority, created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING run.id, run.clip_id, run.type, run.video_version, run.input::text AS input, run.attempts,
                      run.lease_id,
                      (SELECT clip.video_version FROM clip WHERE clip.id = run.clip_id) AS current_video_version
            """;
        return await connection.QuerySingleOrDefaultAsync<ClaimedRunRow>(sql,
            new { MaxAttempts = maxAttempts, StaleAfter = staleAfter });
    }

    /// <summary>
    /// Records a progress report and counts it as a heartbeat. The percentage never goes back within an attempt,
    /// so a delayed report cannot undo a later one. Null when the job is not running under this lease.
    /// </summary>
    public async Task<ProgressRunRow?> ReportProgressAsync(Guid runId, Guid leaseId, int percent, bool thumbnailsReady)
    {
        // The locked subquery reads the row as it was, to tell whether this report is the first with thumbnails.
        const string sql = """
            UPDATE transcode_run run
            SET progress = GREATEST(COALESCE(run.progress, 0), @Percent),
                heartbeat_at = now(),
                thumbnails_ready_at = CASE WHEN @ThumbnailsReady THEN COALESCE(run.thumbnails_ready_at, now())
                                           ELSE run.thumbnails_ready_at END
            FROM (SELECT id, thumbnails_ready_at FROM transcode_run WHERE id = @RunId FOR UPDATE) old_run
            WHERE run.id = old_run.id AND run.lease_id = @LeaseId AND run.status = 'running'
            RETURNING run.clip_id, run.type, run.progress,
                      (@ThumbnailsReady AND old_run.thumbnails_ready_at IS NULL) AS thumbnails_newly_ready
            """;
        return await connection.QuerySingleOrDefaultAsync<ProgressRunRow>(sql, new
        {
            RunId = runId, LeaseId = leaseId, Percent = percent, ThumbnailsReady = thumbnailsReady
        });
    }

    /// <summary>
    /// Marks the job succeeded with what the worker reported, keeping the count of earlier releases
    /// (<see cref="ReleaseRunAsync"/>). Null when it is not running under this lease.
    /// </summary>
    public async Task<FinishedRunRow?> CompleteRunAsync(Guid runId, Guid leaseId, string outputJson)
    {
        const string sql = """
            UPDATE transcode_run
            SET status = 'succeeded',
                output = jsonb_strip_nulls(jsonb_build_object('release_count', output -> 'release_count'))
                         || @Output::jsonb,
                progress = 100,
                error = NULL,
                heartbeat_at = now(),
                finished_at = now()
            WHERE id = @RunId AND lease_id = @LeaseId AND status = 'running'
            RETURNING id, clip_id, type, status, video_version
            """;
        return await connection.QuerySingleOrDefaultAsync<FinishedRunRow>(sql,
            new { RunId = runId, LeaseId = leaseId, Output = outputJson });
    }

    /// <summary>
    /// Records a failed attempt. A retryable failure with attempts left goes back to pending and waits
    /// <paramref name="retryAfter"/>; anything else fails the job. Null when it is not running under this lease.
    /// </summary>
    public async Task<FinishedRunRow?> FailRunAsync(
        Guid runId,
        Guid leaseId,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryAfter)
    {
        const string sql = """
            UPDATE transcode_run
            SET status = CASE WHEN @Retryable AND attempts < @MaxAttempts THEN 'pending' ELSE 'failed' END,
                available_at = CASE WHEN @Retryable AND attempts < @MaxAttempts THEN now() + @RetryAfter
                                    ELSE available_at END,
                finished_at = CASE WHEN @Retryable AND attempts < @MaxAttempts THEN NULL ELSE now() END,
                error = @Error
            WHERE id = @RunId AND lease_id = @LeaseId AND status = 'running'
            RETURNING id, clip_id, type, status, video_version
            """;
        return await connection.QuerySingleOrDefaultAsync<FinishedRunRow>(sql, new
        {
            RunId = runId, LeaseId = leaseId, Error = error, Retryable = retryable, MaxAttempts = maxAttempts,
            RetryAfter = retryAfter
        });
    }

    /// <summary>
    /// Hands a job back from a worker that is shutting down: pending again and claimable at once, with no lease,
    /// heartbeat or progress. The attempt the claim counted is refunded (never below 0) while the job has been
    /// released at most <paramref name="maxReleases"/> times; past that it is kept, so a worker restarted for ever
    /// runs the job out of attempts. The count and the lease it was released under are kept in <c>output</c>
    /// (<c>release_count</c>, <c>released_lease_id</c>), which a pending job doesn't otherwise use, so a repeat of
    /// the report can be told from a stale lease. Null when the job is not running under this lease.
    /// </summary>
    public async Task<ReleasedRunRow?> ReleaseRunAsync(Guid runId, Guid leaseId, int maxReleases)
    {
        // SET reads the row as it was; RETURNING reads it as updated, so release_count there includes this release.
        const string sql = """
            UPDATE transcode_run
            SET status = 'pending',
                available_at = now(),
                attempts = CASE WHEN COALESCE((output ->> 'release_count')::int, 0) < @MaxReleases
                                THEN GREATEST(attempts - 1, 0) ELSE attempts END,
                lease_id = NULL,
                heartbeat_at = NULL,
                progress = NULL,
                output = COALESCE(output, '{}'::jsonb) || jsonb_build_object(
                    'release_count', COALESCE((output ->> 'release_count')::int, 0) + 1,
                    'released_lease_id', @LeaseId::text)
            WHERE id = @RunId AND lease_id = @LeaseId AND status = 'running'
            RETURNING id, clip_id, type, attempts, (output ->> 'release_count')::int AS release_count,
                      (output ->> 'release_count')::int <= @MaxReleases AS refunded
            """;
        return await connection.QuerySingleOrDefaultAsync<ReleasedRunRow>(sql,
            new { RunId = runId, LeaseId = leaseId, MaxReleases = maxReleases });
    }

    public async Task<RunStateRow?> GetRunAsync(Guid runId)
    {
        const string sql = """
            SELECT id, clip_id, type, status, lease_id, video_version,
                   (output ->> 'released_lease_id')::uuid AS released_lease_id
            FROM transcode_run WHERE id = @RunId
            """;
        return await connection.QuerySingleOrDefaultAsync<RunStateRow>(sql, new { RunId = runId });
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction on this connection. Statements classes resolved from the same
    /// request scope share the connection (see <c>AddPersistence</c>), so their queries inside take part in it:
    /// Npgsql runs every command on an open connection inside its current transaction.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        // Dapper opens the connection for each query on its own, but a transaction needs it open throughout.
        bool openedConnection = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        try
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            T result = await work();
            await transaction.CommitAsync();
            return result;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public sealed class ClaimedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public int VideoVersion { get; set; }
        public string Input { get; set; } = "{}";
        public int Attempts { get; set; }
        public Guid LeaseId { get; set; }
        public int CurrentVideoVersion { get; set; } = 1;
    }

    public sealed class ProgressRunRow
    {
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public int Progress { get; set; }
        public bool ThumbnailsNewlyReady { get; set; }
    }

    public sealed class FinishedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public int VideoVersion { get; set; } = 1;
    }

    public sealed class RunStateRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public Guid? LeaseId { get; set; }
        public int VideoVersion { get; set; } = 1;

        /// <summary>The lease the job was last handed back under by a shutting-down worker.</summary>
        public Guid? ReleasedLeaseId { get; set; }
    }

    public sealed class ReleasedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public int Attempts { get; set; }
        public int ReleaseCount { get; set; }
        public bool Refunded { get; set; }
    }
}
