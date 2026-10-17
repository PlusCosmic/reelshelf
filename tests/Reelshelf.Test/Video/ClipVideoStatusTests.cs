using System.Text.Json;
using Dapper;
using Npgsql;
using Reelshelf.Bunny;
using Reelshelf.Core;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Video;

public class ClipVideoStatusTests
{
    [Fact]
    public void DatabaseSpelling_RoundTrips_AndMatchesTheJson()
    {
        foreach (ClipVideoStatus status in Enum.GetValues<ClipVideoStatus>())
        {
            string stored = status.ToDatabase();
            Assert.Equal(status, ClipVideoStatuses.FromDatabase(stored));
            Assert.Equal($"\"{stored}\"", JsonSerializer.Serialize(status));
        }

        Assert.Equal("awaiting_upload", ClipVideoStatus.AwaitingUpload.ToDatabase());
        Assert.Equal("encode_failed", ClipVideoStatus.EncodeFailed.ToDatabase());
    }

    [Fact]
    public void OnlyReadyIsPlayable()
    {
        Assert.Equal([ClipVideoStatus.Ready], Enum.GetValues<ClipVideoStatus>().Where(s => s.IsPlayable()));
    }

    /// <summary>
    /// Runs the V38 migration against a temporary <c>clip</c> table holding every <c>video_status</c> the old app
    /// could have stored, and checks the backfill agrees with the Bunny host's mapping.
    /// </summary>
    [PostgresFact]
    public async Task Migration_BackfillsVideoState_LikeTheBunnyMapping()
    {
        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(PostgresFactAttribute.ConnectionString!);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TEMP TABLE clip (id serial PRIMARY KEY, video_status integer)");

        int?[] statuses = [null, 0, 1, 2, 3, 4, 5, 6, 7, 8, 42];
        foreach (int? status in statuses)
        {
            await connection.ExecuteAsync("INSERT INTO clip (video_status) VALUES (@status)", new { status });
        }

        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V38__clip_video_state.sql")));

        List<(int? VideoStatus, string VideoState)> rows = (await connection.QueryAsync<(int?, string)>(
            "SELECT video_status, video_state FROM clip ORDER BY id")).ToList();
        Assert.Equal(statuses.Length, rows.Count);
        foreach ((int? videoStatus, string videoState) in rows)
        {
            ClipVideoStatus expected = videoStatus is { } bunny
                ? BunnyService.ToClipVideoStatus(bunny)
                : ClipVideoStatus.AwaitingUpload;
            Assert.Equal(expected.ToDatabase(), videoState);
        }

        // A clip the previous app image reserves after the migration gets the default.
        Assert.Equal("awaiting_upload", await connection.QuerySingleAsync<string>(
            "INSERT INTO clip (video_status) VALUES (NULL) RETURNING video_state"));
        await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("UPDATE clip SET video_state = 'finished'"));
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
