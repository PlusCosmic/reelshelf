using Dapper;
using Npgsql;
using Pgvector.Npgsql;
using Reelshelf.ClipSummary;

namespace Reelshelf.Core;

public static class ReelshelfDataSource
{
    /// <summary>
    /// The data source every connection comes from, with pgvector's <c>vector</c> type mapped for Npgsql and Dapper.
    /// </summary>
    public static NpgsqlDataSource Create(string connectionString)
    {
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
        NpgsqlDataSourceBuilder builder = new(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}
