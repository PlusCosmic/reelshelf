using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Games;
using Reelshelf.Test.ApexLegends;
using Reelshelf.Test.Core;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Reads clips through the statements the clip pages use, against a real Postgres, to check what
/// <c>thumbnails_ready</c> says (<see cref="Reelshelf.Video.ClipVideoColumns.ThumbnailsReadySql"/>) and so what the
/// projected clip hands out: an R2 clip's thumbnail only once its worker reported it or the clip is ready, and
/// sources only once it is ready. Runs in a schema of the test's own, dropped afterwards.
/// </summary>
public class ClipThumbnailsReadyTests
{
    private static readonly GameCategory Category = new() { Id = Guid.NewGuid(), Name = "Apex Legends", Slug = "apex-legends" };

    [PostgresFact]
    public async Task AnR2Clip_HasNoThumbnailOrSources_UntilTheWorkerReportsThumbnails()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid clipId = await test.AddClipAsync("r2", "processing");
        Guid runId = await test.AddRunAsync(clipId, videoVersion: 1);

        ClipVideo before = await test.ProjectAsync(clipId);
        Assert.Null(before.ThumbnailUrl);
        Assert.Empty(before.Sources);

        await test.Connection.ExecuteAsync(
            "UPDATE transcode_run SET thumbnails_ready_at = now() WHERE id = @RunId", new { RunId = runId });

        ClipVideo after = await test.ProjectAsync(clipId);
        Assert.Equal($"https://media.example/clips/{clipId}/v1/thumbnail.jpg", after.ThumbnailUrl);
        // Still encoding: there is no av1.mp4 to play yet.
        Assert.Empty(after.Sources);
    }

    [PostgresFact]
    public async Task ThumbnailsOfAnotherVersion_DoNotCount()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid clipId = await test.AddClipAsync("r2", "processing");
        await test.AddRunAsync(clipId, videoVersion: 2, thumbnailsReady: true);

        ClipsStatements.ClipWithTagsRow row = Assert.Single((await test.Clips.GetClipsWithTags(test.OwnerId, null)).Rows);
        Assert.False(row.ThumbnailsReady);
        Assert.Null(test.Projection.ProjectClip(row, Category, false, false).Video.ThumbnailUrl);
    }

    [PostgresFact]
    public async Task AReadyClip_HasItsThumbnailAndSources_WithoutAnyRun()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid r2ClipId = await test.AddClipAsync("r2", "ready");
        Guid bunnyClipId = await test.AddClipAsync("bunny", "ready");

        ClipVideo r2 = await test.ProjectAsync(r2ClipId);
        Assert.Equal($"https://media.example/clips/{r2ClipId}/v1/thumbnail.jpg", r2.ThumbnailUrl);
        Assert.Equal($"https://media.example/clips/{r2ClipId}/v1/av1.mp4", Assert.Single(r2.Sources).Url);

        ClipVideo bunny = await test.ProjectAsync(bunnyClipId);
        Assert.Equal($"https://cdn.example/{bunnyClipId}/thumbnail.jpg", bunny.ThumbnailUrl);
        Assert.Equal($"https://cdn.example/{bunnyClipId}/playlist.m3u8", Assert.Single(bunny.Sources).Url);
    }

    [PostgresFact]
    public async Task AProcessingBunnyClip_KeepsItsThumbnail_ButHasNoSources()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid clipId = await test.AddClipAsync("bunny", "processing");

        ClipVideo video = await test.ProjectAsync(clipId);
        Assert.Equal($"https://cdn.example/{clipId}/thumbnail.jpg", video.ThumbnailUrl);
        Assert.Empty(video.Sources);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestDatabase(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            _dataSource = dataSource;
            _schema = schema;
            Connection = connection;
            Clips = new ClipsStatements(dataSource.CreateConnection());
        }

        public NpgsqlConnection Connection { get; }

        public ClipsStatements Clips { get; }

        public ClipProjection Projection { get; } =
            new(ClipMediaUrlsTests.Create("https://cdn.example", "https://media.example"));

        public Guid OwnerId { get; } = Guid.NewGuid();

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"thumbnails_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            // The tag tables the clip queries join, the game categories their search reads, and the legend
            // tables an Apex clip's legend comes from.
            await connection.ExecuteAsync("""
                CREATE TABLE tag (id uuid PRIMARY KEY, name text NOT NULL);
                CREATE TABLE clip_tag (clip_id uuid NOT NULL, tag_id uuid NOT NULL);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL, slug text NOT NULL);
                """);
            await ClipLegendTests.CreateLegendTablesAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(string videoHost, string videoState)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', @VideoState, @VideoHost)
                """, new { ClipId = clipId, OwnerId, GameCategoryId = Category.Id, VideoState = videoState, VideoHost = videoHost });
            return clipId;
        }

        public async Task<Guid> AddRunAsync(Guid clipId, int videoVersion, bool thumbnailsReady = false) =>
            await Connection.ExecuteScalarAsync<Guid>("""
                INSERT INTO transcode_run (clip_id, type, status, video_version, thumbnails_ready_at)
                VALUES (@ClipId, 'transcode', 'running', @VideoVersion, CASE WHEN @ThumbnailsReady THEN now() END)
                RETURNING id
                """, new { ClipId = clipId, VideoVersion = videoVersion, ThumbnailsReady = thumbnailsReady });

        /// <summary>The clip as its own page gets it.</summary>
        public async Task<ClipVideo> ProjectAsync(Guid clipId)
        {
            ClipsStatements.ClipWithTagsRow row = Assert.IsType<ClipsStatements.ClipWithTagsRow>(
                await Clips.GetClipWithTagsByIdAndOwner(clipId, OwnerId));
            return Projection.ProjectClip(row, Category, isViewed: false, isShared: false).Video;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
            await using NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
        }
    }
}
