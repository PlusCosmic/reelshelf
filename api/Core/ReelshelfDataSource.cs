using Dapper;
using Npgsql;
using Pgvector.Npgsql;
using Reelshelf.ClipSummary;

namespace Reelshelf.Core;

public static class ReelshelfDataSource
{
    /// <summary>
    /// The pool's name in Npgsql's metrics (<c>Npgsql</c> meter). Without one, Npgsql names a pool after its connection
    /// string less the password, which would put the database's host, name and user on every pool series.
    /// </summary>
    public const string PoolName = "reelshelf";

    /// <summary>The health check's own pool, so a busy main pool doesn't fail <c>/health</c>.</summary>
    public const string HealthCheckPoolName = "reelshelf-health";

    /// <summary>
    /// The data source every connection comes from, with pgvector's <c>vector</c> type mapped for Npgsql and Dapper.
    /// </summary>
    public static NpgsqlDataSource Create(string connectionString)
    {
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
        NpgsqlDataSourceBuilder builder = new(connectionString) { Name = PoolName };
        builder.UseVector();
        return builder.Build();
    }

    /// <summary>The health check's data source: a plain pool named <see cref="HealthCheckPoolName"/>.</summary>
    public static NpgsqlDataSource CreateHealthCheck(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString) { Name = HealthCheckPoolName }.Build();
}
