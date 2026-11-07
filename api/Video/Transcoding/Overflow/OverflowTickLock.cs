using Dapper;
using Npgsql;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// Lets one API instance at a time run an <see cref="OverflowScaler"/> tick. A deploy runs the old and the new
/// instance side by side for a while; both would read the same backlog and the same running counts and each start
/// the tasks it lacks, so one tick's tasks would be started twice. The pools' caps don't stop that, because the
/// count each tick reads is taken before either instance starts anything.
/// </summary>
public interface IOverflowTickLock
{
    /// <summary>Runs <paramref name="tick"/> under the lock; false, without running it, while another instance holds it.</summary>
    Task<bool> TryRunAsync(Func<CancellationToken, Task> tick, CancellationToken cancellationToken);
}

/// <summary>
/// A transaction-scoped Postgres advisory lock, held on its own connection for the whole tick, so it is released
/// however the tick ends, the connection dropping included.
/// </summary>
public sealed class PostgresOverflowTickLock(NpgsqlDataSource dataSource) : IOverflowTickLock
{
    internal const string Key = "overflow-scaler";

    public async Task<bool> TryRunAsync(Func<CancellationToken, Task> tick, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        bool acquired = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_try_advisory_xact_lock(hashtext(@key))", new { key = Key }, transaction,
            cancellationToken: cancellationToken));
        if (!acquired)
        {
            return false;
        }

        await tick(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
