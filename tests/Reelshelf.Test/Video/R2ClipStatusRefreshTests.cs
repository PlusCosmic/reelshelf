using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// <see cref="ClipStatusRefreshService"/> with R2 clips beside Bunny ones, against a real Postgres. R2 has no video
/// object, so the refresh must never take its clips for abandoned the way it does a Bunny video the host has lost.
/// None of these touch R2 itself: nothing here is deleted from it.
/// </summary>
public class R2ClipStatusRefreshTests
{
    [PostgresFact]
    public async Task Refresh_LeavesR2ClipsAlone_AndStillPurgesALostBunnyVideo()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        TimeSpan old = TimeSpan.FromDays(2);
        Guid uploaded = await test.AddClipAsync(VideoHostNames.R2, "uploaded", reservedAgo: old);
        Guid processing = await test.AddClipAsync(VideoHostNames.R2, "processing", reservedAgo: old, encodeProgress: 40);
        Guid fresh = await test.AddClipAsync(VideoHostNames.R2, "awaiting_upload", reservedAgo: TimeSpan.FromHours(1));
        Guid lostBunny = await test.AddClipAsync(VideoHostNames.Bunny, "awaiting_upload", reservedAgo: old);

        await test.Service.RefreshClipStatusesAsync();

        // Every R2 clip is still there as it was, with the legacy video_status still unwritten; the Bunny clip whose
        // video the host no longer has was purged, so the purge did run.
        List<(Guid Id, string State, int? Progress, int? VideoStatus)> expected =
        [
            (uploaded, "uploaded", 0, null),
            (processing, "processing", 40, null),
            (fresh, "awaiting_upload", 0, null)
        ];
        Assert.Equal(expected, await test.ClipsAsync());
        // Only the Bunny clip was looked up at its host; R2 clips are never polled.
        Assert.Equal([lostBunny], test.Bunny.Fetched);
    }

    [PostgresFact]
    public async Task GetVideo_ReportsTheR2ClipsRealStateFromItsRow()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid processing = await test.AddClipAsync(VideoHostNames.R2, "processing", reservedAgo: TimeSpan.Zero,
            encodeProgress: 40);

        using IServiceScope scope = test.Provider.CreateScope();
        IClipVideoHost host = scope.ServiceProvider.GetRequiredService<ClipVideoHosts>().For(VideoHostNames.R2);
        HostedVideoInfo? video = await host.GetVideoAsync(processing);

        Assert.NotNull(video);
        Assert.Equal((ClipVideoStatus.Processing, 40, (int?)null), (video.Status, video.EncodeProgress, video.HostStatus));
        Assert.False(ClipStatusRefreshService.IsAbandoned(video));
        Assert.False(host.NeedsStatusPolling);
        Assert.Null(await host.GetVideoAsync(Guid.NewGuid()));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestDatabase(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema,
            ServiceProvider provider, FakeBunny bunny)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Provider = provider;
            Bunny = bunny;
            Service = new ClipStatusRefreshService(NullLogger<ClipStatusRefreshService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>());
        }

        public NpgsqlConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeBunny Bunny { get; }
        public ClipStatusRefreshService Service { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"r2_refresh_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);

            FakeBunny bunny = new();
            R2Storage storage = new(new R2Options
            {
                AccountId = "test", AccessKeyId = "id", SecretAccessKey = "secret", Bucket = "bucket",
                MediaBaseUrl = "https://media.example"
            }, NullLogger<R2Storage>.Instance);
            ServiceCollection services = new();
            services.AddScoped(_ => dataSource.CreateConnection());
            services.AddScoped<ClipsStatements>();
            services.AddScoped<ClipVideoHosts>();
            services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, bunny);
            // Uploads (and so the queue) aren't used by the refresh.
            services.AddKeyedScoped<IClipVideoHost>(VideoHostNames.R2, (provider, _) => new R2VideoHost(storage,
                provider.GetRequiredService<ClipsStatements>(), null!, null!, TimeProvider.System,
                NullLogger<R2VideoHost>.Instance));
            return new TestDatabase(dataSource, connection, schema, services.BuildServiceProvider(), bunny);
        }

        public async Task<Guid> AddClipAsync(string videoHost, string videoState, TimeSpan reservedAgo,
            int encodeProgress = 0)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host,
                                  encode_progress, storage_size, reserved_at)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', @VideoState, @VideoHost,
                        @EncodeProgress, 0, now() - @ReservedAgo)
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(), VideoState = videoState,
                VideoHost = videoHost, EncodeProgress = encodeProgress, ReservedAgo = reservedAgo
            });
            return clipId;
        }

        public async Task<List<(Guid Id, string State, int? Progress, int? VideoStatus)>> ClipsAsync() =>
            (await Connection.QueryAsync<(Guid, string, int?, int?)>(
                "SELECT id, video_state, encode_progress, video_status FROM clip ORDER BY reserved_at, video_state DESC"))
            .ToList();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                await Provider.DisposeAsync();
                await Connection.DisposeAsync();
                await _dataSource.DisposeAsync();
            }
        }
    }

    /// <summary>A Bunny that has lost every video.</summary>
    private sealed class FakeBunny : IClipVideoHost
    {
        public List<Guid> Fetched { get; } = [];

        public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title) =>
            throw new NotSupportedException();

        public UploadTarget CreateUploadTarget(HostedVideo video, string title) => throw new NotSupportedException();

        public Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HostedVideoInfo?> GetVideoAsync(Guid videoId)
        {
            Fetched.Add(videoId);
            return Task.FromResult<HostedVideoInfo?>(null);
        }

        public Task DeleteVideoAsync(Guid videoId) => Task.CompletedTask;

        public Task DeleteContainerAsync(Guid containerId) => Task.CompletedTask;
    }
}
