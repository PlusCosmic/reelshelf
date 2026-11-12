using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// <see cref="ClipStatusRefreshService"/> with R2 clips, against a real Postgres. R2 has no video object, so the
/// refresh must take a clip for abandoned only when its row says nothing was uploaded.
/// None of these touch R2 itself: nothing here is deleted from it.
/// </summary>
public class R2ClipStatusRefreshTests
{
    [PostgresFact]
    public async Task Refresh_LeavesR2ClipsThatGotTheirBytesAlone()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        TimeSpan old = TimeSpan.FromDays(2);
        Guid uploaded = await test.AddClipAsync(VideoHostNames.R2, "uploaded", reservedAgo: old);
        Guid processing = await test.AddClipAsync(VideoHostNames.R2, "processing", reservedAgo: old, encodeProgress: 40);
        Guid fresh = await test.AddClipAsync(VideoHostNames.R2, "awaiting_upload", reservedAgo: TimeSpan.FromHours(1));

        await test.Service.RefreshClipStatusesAsync();

        // Every clip is still there as it was.
        List<(Guid Id, string State, int? Progress)> expected =
        [
            (uploaded, "uploaded", 0),
            (processing, "processing", 40),
            (fresh, "awaiting_upload", 0)
        ];
        Assert.Equal(expected, await test.ClipsAsync());
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
        Assert.Equal((ClipVideoStatus.Processing, 40), (video.Status, video.EncodeProgress));
        Assert.False(ClipStatusRefreshService.IsAbandoned(video));
        Assert.Null(await host.GetVideoAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Purge_AbortsAnAbandonedBrowserUpload_AndRemovesTheClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid abandoned = await test.AddClipAsync(VideoHostNames.R2, "awaiting_upload", reservedAgo: TimeSpan.FromDays(2));
        Guid fresh = await test.AddClipAsync(VideoHostNames.R2, "awaiting_upload", reservedAgo: TimeSpan.FromHours(1));
        string abandonedUpload = test.R2.Open(R2ObjectKeys.Upload(abandoned));
        test.R2.PutPart(abandonedUpload, 1, 8 * 1024 * 1024);
        string freshUpload = test.R2.Open(R2ObjectKeys.Upload(fresh));

        await test.Service.RefreshClipStatusesAsync();

        // The day-old upload's parts are freed and its clip is gone; the one still in progress is untouched.
        Assert.Equal([abandonedUpload], test.R2.Aborted);
        Assert.Equal([freshUpload], test.R2.Uploads.Keys);
        Assert.Equal([fresh], (await test.ClipsAsync()).Select(clip => clip.Id));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestDatabase(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema,
            ServiceProvider provider, FakeR2Client r2)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Provider = provider;
            R2 = r2;
            Service = new ClipStatusRefreshService(NullLogger<ClipStatusRefreshService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>());
        }

        public NpgsqlConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeR2Client R2 { get; }
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

            FakeR2Client r2 = new();
            R2Storage storage = r2.Storage();
            ServiceCollection services = new();
            services.AddScoped(_ => dataSource.CreateConnection());
            services.AddScoped<ClipsStatements>();
            services.AddScoped<ClipVideoHosts>();
            // Uploads (and so the queue and the probe) aren't used by the refresh.
            services.AddKeyedScoped<IClipVideoHost>(VideoHostNames.R2, (provider, _) => new R2VideoHost(storage,
                provider.GetRequiredService<ClipsStatements>(), null!, null!, null!, TimeProvider.System,
                NullLogger<R2VideoHost>.Instance));
            return new TestDatabase(dataSource, connection, schema, services.BuildServiceProvider(), r2);
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

        public async Task<List<(Guid Id, string State, int? Progress)>> ClipsAsync() =>
            (await Connection.QueryAsync<(Guid, string, int?)>(
                "SELECT id, video_state, encode_progress FROM clip ORDER BY reserved_at, video_state DESC"))
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

}
