using Dapper;
using Npgsql;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.Migration;

/// <summary>Queues <c>migrate</c> jobs on <c>transcode_run</c> for the clips still on Bunny.</summary>
public class BunnyMigrationStatements(NpgsqlConnection connection)
{
    /// <summary>
    /// Tops the <c>migrate</c> jobs pending or running up to <paramref name="batchSize"/>, taking ready Bunny clips
    /// oldest first, and returns the clips queued. A clip is skipped while it has any job pending or running, and for
    /// good once it has had a <c>migrate</c> job: a failed one is left for a person to look at, and a succeeded one
    /// whose clip is on Bunny again was switched back on purpose. Deleting the run lets the clip be queued again. Two
    /// instances backfilling at once take turns on an advisory lock, so neither overshoots the batch.
    /// </summary>
    public async Task<List<Guid>> QueueBatchAsync(int batchSize)
    {
        const string countActive = """
            SELECT COUNT(*) FROM transcode_run
            WHERE type = 'migrate' AND status IN ('pending', 'running')
            """;
        const string queue = """
            INSERT INTO transcode_run (clip_id, type, input, video_version, priority)
            SELECT clip.id, 'migrate', '{}'::jsonb, @VideoVersion, @Priority
            FROM clip
            WHERE clip.video_host = 'bunny'
              AND clip.video_state = 'ready'
              AND NOT EXISTS (
                  SELECT 1 FROM transcode_run run
                  WHERE run.clip_id = clip.id
                    AND (run.status IN ('pending', 'running') OR run.type = 'migrate'))
            ORDER BY clip.created_at, clip.id
            LIMIT @Limit
            RETURNING clip_id
            """;

        bool openedConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        try
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('transcode_run:migrate_backfill'))",
                transaction: transaction);
            long active = await connection.ExecuteScalarAsync<long>(countActive, transaction: transaction);
            long limit = batchSize - active;
            List<Guid> queued = limit <= 0
                ? []
                : (await connection.QueryAsync<Guid>(queue, new
                {
                    VideoVersion = TranscodeQueue.MigratedVideoVersion,
                    Priority = TranscodeRunTypes.DefaultPriority(TranscodeRunTypes.Migrate),
                    Limit = limit
                }, transaction)).ToList();
            await transaction.CommitAsync();
            return queued;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
