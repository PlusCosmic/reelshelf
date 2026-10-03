using Dapper;
using Npgsql;
using Pgvector;
using Reelshelf.ClipSummary;
using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.ClipSummary;

public class EmbeddingStorageTests
{
    [PostgresFact]
    public async Task Embedding_RoundTripsThroughAVectorColumn()
    {
        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(PostgresFactAttribute.ConnectionString!);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector");
        // UseVector() loads the type when the data source first connects, which may be before the extension existed.
        await connection.ReloadTypesAsync();
        await connection.ExecuteAsync(
            $"CREATE TEMP TABLE embedding_round_trip (id int, embedding vector({ClipSummarizerFactory.EmbeddingDimensions}))");

        float[] values = Enumerable.Range(0, ClipSummarizerFactory.EmbeddingDimensions)
            .Select(i => MathF.Sin(i) / 3)
            .ToArray();
        await connection.ExecuteAsync("INSERT INTO embedding_round_trip VALUES (1, @Embedding), (2, NULL)",
            new { Embedding = new Vector(values) });

        Vector? stored = await connection.QuerySingleAsync<Vector?>(
            "SELECT embedding FROM embedding_round_trip WHERE id = 1");
        Vector? missing = await connection.QuerySingleAsync<Vector?>(
            "SELECT embedding FROM embedding_round_trip WHERE id = 2");
        double distance = await connection.QuerySingleAsync<double>(
            "SELECT embedding <=> @Embedding FROM embedding_round_trip WHERE id = 1",
            new { Embedding = new Vector(values) });

        Assert.Equal(values, stored!.ToArray());
        Assert.Null(missing);
        Assert.Equal(0, distance, precision: 6);
    }
}
