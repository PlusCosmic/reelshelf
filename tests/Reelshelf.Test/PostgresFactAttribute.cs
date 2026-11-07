using Dapper;
using Npgsql;
using Xunit;

namespace Reelshelf.Test;

/// <summary>
/// A test that needs a real Postgres with pgvector. It runs when <c>REELSHELF_TEST_DATABASE</c> holds a connection
/// string, as it does in CI, and is skipped otherwise.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "REELSHELF_TEST_DATABASE";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {ConnectionStringVariable} to a Postgres connection string to run this test";
        }
    }

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    /// <summary>
    /// Creates the extensions if they are missing, one test at a time: test classes run in parallel, and two
    /// <c>CREATE EXTENSION IF NOT EXISTS</c> at once can both find it missing and the second fails on
    /// <c>pg_extension_name_index</c>.
    /// </summary>
    public static async Task CreateExtensionsAsync(NpgsqlConnection connection, params string[] extensions)
    {
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('reelshelf-test-extensions'))",
            transaction: transaction);
        foreach (string extension in extensions)
        {
            await connection.ExecuteAsync($"CREATE EXTENSION IF NOT EXISTS {extension}", transaction: transaction);
        }

        await transaction.CommitAsync();
    }
}
