using Dapper;
using Npgsql;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The claimable part of <c>transcode_run</c>: how many jobs a worker could claim right now and how long the oldest
/// of them has been waiting.
/// </summary>
public sealed record OverflowBacklog(long Pending, TimeSpan OldestPendingAge)
{
    public static readonly OverflowBacklog Empty = new(0, TimeSpan.Zero);
}

/// <summary>Reads the backlog the overflow scaler acts on; a fake stands in for it in tests.</summary>
public interface IOverflowBacklogReader
{
    /// <param name="maxAttempts">The claim's attempt limit; jobs that have used it up never count.</param>
    /// <param name="jobTypes">Only these job types; empty counts them all.</param>
    Task<OverflowBacklog> ReadAsync(int maxAttempts, IReadOnlyList<string> jobTypes, CancellationToken cancellationToken);
}

/// <summary>
/// Counts the pending jobs exactly as <see cref="TranscodeStatements.ClaimNextRunAsync"/> chooses them: pending,
/// past <c>available_at</c>, with attempts left. A job waiting out its retry delay, or one out of attempts that the
/// next claim will fail, is not backlog and never starts a task. Running jobs whose worker went quiet are left out
/// too: they come back on the claim's stale reclaim, which a running worker does on its next poll.
/// </summary>
public sealed class OverflowBacklogStatements(NpgsqlDataSource dataSource) : IOverflowBacklogReader
{
    /// <summary>
    /// The age is counted from <c>available_at</c>, the moment the job became claimable, rather than its
    /// <c>created_at</c> as the <c>oldest_pending_age</c> gauge does: a job a worker released on shutdown, or one
    /// past its retry delay, has only just become available, so the dedicated worker gets
    /// <see cref="OverflowOptions.ScaleUpAfter"/> to take it before a task is started for it. The status condition
    /// matches <c>transcode_run_pending_idx</c> (V40)'s predicate, so the count reads that partial index.
    /// </summary>
    internal const string Sql = """
        SELECT COUNT(*) AS pending,
               COALESCE(EXTRACT(EPOCH FROM now() - MIN(available_at)), 0)::float8 AS oldest_pending_age_seconds
        FROM transcode_run
        WHERE status = 'pending'
          AND attempts < @MaxAttempts
          AND available_at <= now()
          AND (cardinality(@JobTypes) = 0 OR type = ANY(@JobTypes))
        """;

    public async Task<OverflowBacklog> ReadAsync(
        int maxAttempts,
        IReadOnlyList<string> jobTypes,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        BacklogRow row = await connection.QuerySingleAsync<BacklogRow>(new CommandDefinition(Sql,
            new { MaxAttempts = maxAttempts, JobTypes = jobTypes.ToArray() }, cancellationToken: cancellationToken));
        return new OverflowBacklog(row.Pending, TimeSpan.FromSeconds(Math.Max(0, row.OldestPendingAgeSeconds)));
    }

    internal sealed class BacklogRow
    {
        public long Pending { get; set; }
        public double OldestPendingAgeSeconds { get; set; }
    }
}
