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
}
