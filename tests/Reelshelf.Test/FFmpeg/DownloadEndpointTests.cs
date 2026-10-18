using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.FFmpeg;
using Reelshelf.Test.Video;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.FFmpeg;

/// <summary>
/// <c>GET /api/ffmpeg/download/{videoId}</c>: an R2 clip is a redirect to a presigned URL for its current
/// <c>av1.mp4</c>, a Bunny clip is still remuxed on the server, and either needs the same access as viewing the clip.
/// Presigning signs with the key alone, so nothing here touches R2.
/// </summary>
public class DownloadEndpointTests
{
    private static readonly R2Options Options = new()
    {
        AccountId = "0123456789abcdef",
        AccessKeyId = "test-access-key",
        SecretAccessKey = "test-secret-key",
        Bucket = "reelshelf-dev",
        MediaBaseUrl = "https://media-dev.example"
    };

    [Theory]
    [InlineData("Clip.mp4", "attachment; filename=\"Clip.mp4\"; filename*=UTF-8''Clip.mp4")]
    [InlineData("Ace 1v3 (ranked).mp4",
        "attachment; filename=\"Ace 1v3 (ranked).mp4\"; filename*=UTF-8''Ace%201v3%20%28ranked%29.mp4")]
    [InlineData("Café — 100% it's over.mp4",
        "attachment; filename=\"Caf_ _ 100% it's over.mp4\"; filename*=UTF-8''Caf%C3%A9%20%E2%80%94%20100%25%20it%27s%20over.mp4")]
    [InlineData("清算 🎉.mp4", "attachment; filename=\"__ __.mp4\"; filename*=UTF-8''%E6%B8%85%E7%AE%97%20%F0%9F%8E%89.mp4")]
    [InlineData("a\"b\\c.mp4", "attachment; filename=\"a_b_c.mp4\"; filename*=UTF-8''a%22b%5Cc.mp4")]
    public void TheDisposition_QuotesAnAsciiName_AndCarriesTheExactOneAsRfc5987(string fileName, string expected)
    {
        Assert.Equal(expected, R2Downloads.AttachmentDisposition(fileName));
    }

    [Fact]
    public void TheDownloadUrl_IsAShortLivedGetOfTheCurrentVersionsAv1_AsAnAttachment()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);
        Guid clipId = Guid.NewGuid();

        Uri url = new(new R2Downloads(storage).DownloadUrl(
            new ClipVideoRef(VideoHostNames.R2, clipId, VideoVersion: 3), "Café night.mp4"));

        AssertDownloadUrl(url, clipId, 3, "Café night.mp4");
    }

    [PostgresFact]
    public async Task AnR2Clip_RedirectsItsOwner_ToR2()
    {
        await using DownloadApi api = await DownloadApi.StartAsync();
        Guid owner = Guid.NewGuid();
        Guid clipId = await api.AddClipAsync(owner, VideoHostNames.R2, "ready", "Ace: 1v3 / ranked?", version: 2);

        using HttpResponseMessage response = await api.GetAsync(clipId, owner);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        AssertDownloadUrl(response.Headers.Location!, clipId, 2, "Ace 1v3 ranked.mp4");
        Assert.Empty(api.Remux.Downloaded);
    }

    [PostgresFact]
    public async Task AnR2Clip_RedirectsAPlaylistCollaborator()
    {
        await using DownloadApi api = await DownloadApi.StartAsync();
        Guid collaborator = Guid.NewGuid();
        Guid clipId = await api.AddClipAsync(Guid.NewGuid(), VideoHostNames.R2, "ready", "Shared");
        await api.SharePlaylistAsync(clipId, collaborator);

        using HttpResponseMessage response = await api.GetAsync(clipId, collaborator);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        AssertDownloadUrl(response.Headers.Location!, clipId, 1, "Shared.mp4");
    }

    [PostgresFact]
    public async Task AnR2Clip_IsNotFound_ForAStranger_OrBeforeItIsReady()
    {
        await using DownloadApi api = await DownloadApi.StartAsync();
        Guid owner = Guid.NewGuid();
        Guid ready = await api.AddClipAsync(owner, VideoHostNames.R2, "ready", "Mine");
        Guid processing = await api.AddClipAsync(owner, VideoHostNames.R2, "processing", "Encoding");

        using HttpResponseMessage stranger = await api.GetAsync(ready, Guid.NewGuid());
        using HttpResponseMessage notReady = await api.GetAsync(processing, owner);
        using HttpResponseMessage unknown = await api.GetAsync(Guid.NewGuid(), owner);
        using HttpResponseMessage signedOut = await api.GetAsync(ready, user: null);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Null(stranger.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, notReady.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }

    [PostgresFact]
    public async Task ABunnyClip_IsStillRemuxedOnTheServer()
    {
        await using DownloadApi api = await DownloadApi.StartAsync();
        Guid owner = Guid.NewGuid();
        Guid clipId = await api.AddClipAsync(owner, VideoHostNames.Bunny, "ready", "Bunny clip");

        using HttpResponseMessage response = await api.GetAsync(clipId, owner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bunny clip.mp4", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(FakeRemux.Content, await response.Content.ReadAsStringAsync());
        ClipVideoRef remuxed = Assert.Single(api.Remux.Downloaded);
        Assert.Equal((VideoHostNames.Bunny, clipId), (remuxed.VideoHost, remuxed.VideoId));
    }

    [PostgresFact]
    public async Task ABunnyClip_IsRemuxedForACollaborator_ButNotForAStranger()
    {
        await using DownloadApi api = await DownloadApi.StartAsync();
        Guid collaborator = Guid.NewGuid();
        Guid clipId = await api.AddClipAsync(Guid.NewGuid(), VideoHostNames.Bunny, "ready", "Bunny clip");
        await api.SharePlaylistAsync(clipId, collaborator);

        using HttpResponseMessage stranger = await api.GetAsync(clipId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Empty(api.Remux.Downloaded);

        using HttpResponseMessage shared = await api.GetAsync(clipId, collaborator);
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Single(api.Remux.Downloaded);
    }

    private static void AssertDownloadUrl(Uri url, Guid clipId, int version, string fileName)
    {
        Assert.Equal("https", url.Scheme);
        Assert.Equal("0123456789abcdef.r2.cloudflarestorage.com", url.Host);
        Assert.Equal($"/reelshelf-dev/clips/{clipId}/v{version}/av1.mp4", url.AbsolutePath);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("600", query["X-Amz-Expires"]);
        Assert.Equal("host", query["X-Amz-SignedHeaders"]);
        Assert.EndsWith("/auto/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.Equal(R2Downloads.AttachmentDisposition(fileName), query["response-content-disposition"]);
        Assert.False(string.IsNullOrEmpty(query["X-Amz-Signature"]));
        Assert.DoesNotContain("test-secret-key", url.ToString());
    }

    /// <summary>
    /// The endpoint as mapped, served by Kestrel on a loopback port against a schema of its own, with a site scheme
    /// that signs in whoever <c>X-Test-User</c> names.
    /// </summary>
    private sealed class DownloadApi : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly NpgsqlDataSource _dataSource;
        private readonly NpgsqlConnection _connection;
        private readonly string _schema;
        private readonly R2Storage _storage;

        private DownloadApi(WebApplication app, HttpClient client, NpgsqlDataSource dataSource,
            NpgsqlConnection connection, string schema, R2Storage storage, FakeRemux remux)
        {
            _app = app;
            _client = client;
            _dataSource = dataSource;
            _connection = connection;
            _schema = schema;
            _storage = storage;
            Remux = remux;
        }

        public FakeRemux Remux { get; }

        public static async Task<DownloadApi> StartAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"download_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync("""
                CREATE TABLE playlists (id uuid PRIMARY KEY, creator_user_id uuid NOT NULL);
                CREATE TABLE playlist_collaborators (playlist_id uuid NOT NULL, user_id uuid NOT NULL);
                CREATE TABLE playlist_clips (playlist_id uuid NOT NULL, clip_id uuid NOT NULL);
                """);

            R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddAuthentication(TestUserScheme.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestUserScheme>(TestUserScheme.SchemeName, null);
            builder.Services.AddAuthorization();
            builder.Services.AddScoped(_ => dataSource.CreateConnection());
            builder.Services.AddScoped<ClipsStatements>();
            builder.Services.AddScoped<ClipVideoHosts>();
            builder.Services.AddKeyedSingleton<IClipVideoDownloads>(VideoHostNames.R2, new R2Downloads(storage));
            builder.Services.AddSingleton<FakeRemux>();
            builder.Services.AddScoped<FFmpegService>(provider => provider.GetRequiredService<FakeRemux>().Service(provider));

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                // What AuthenticatedUserMiddleware does for a signed-in account, without the users table.
                if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
                {
                    context.Items[AuthenticatedUser.HttpContextKey] = new AuthenticatedUser(userId, "user", null, null,
                        null, true, UserRole.Viewer, [], []);
                }

                await next(context);
            });
            app.UseAuthorization();
            app.MapGroup("/api").MapFFmpegEndpoints();
            await app.StartAsync();

            HttpClient client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Urls.First())
            };
            return new DownloadApi(app, client, dataSource, connection, schema, storage,
                app.Services.GetRequiredService<FakeRemux>());
        }

        public async Task<Guid> AddClipAsync(Guid ownerId, string videoHost, string videoState, string title,
            int version = 1)
        {
            Guid clipId = Guid.NewGuid();
            // An R2 clip's video id is its clip id; a Bunny one's is Bunny's, here the same for simplicity.
            await _connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host, video_version)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, @Title, @VideoState, @VideoHost, @Version)
                """, new
            {
                ClipId = clipId, OwnerId = ownerId, GameCategoryId = Guid.NewGuid(), Title = title,
                VideoState = videoState, VideoHost = videoHost, Version = version
            });
            return clipId;
        }

        /// <summary>Puts the clip in someone else's playlist that <paramref name="collaborator"/> collaborates on.</summary>
        public async Task SharePlaylistAsync(Guid clipId, Guid collaborator)
        {
            Guid playlistId = Guid.NewGuid();
            await _connection.ExecuteAsync("""
                INSERT INTO playlists (id, creator_user_id) VALUES (@PlaylistId, @Creator);
                INSERT INTO playlist_collaborators (playlist_id, user_id) VALUES (@PlaylistId, @Collaborator);
                INSERT INTO playlist_clips (playlist_id, clip_id) VALUES (@PlaylistId, @ClipId);
                """, new { PlaylistId = playlistId, Creator = Guid.NewGuid(), Collaborator = collaborator, ClipId = clipId });
        }

        public async Task<HttpResponseMessage> GetAsync(Guid videoId, Guid? user)
        {
            HttpRequestMessage request = new(HttpMethod.Get, $"/api/ffmpeg/download/{videoId}");
            if (user is not null)
            {
                request.Headers.Add(TestUserScheme.Header, user.ToString());
            }

            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                _client.Dispose();
                await _app.StopAsync();
                await _app.DisposeAsync();
                await _connection.DisposeAsync();
                await _dataSource.DisposeAsync();
                _storage.Dispose();
            }
        }
    }

    /// <summary>Stands in for the ffmpeg remux: records what it was asked for and hands back a small file.</summary>
    public sealed class FakeRemux
    {
        public const string Content = "remuxed mp4";

        public List<ClipVideoRef> Downloaded { get; } = [];

        public FFmpegService Service(IServiceProvider provider) => new RecordingFFmpegService(this, provider);

        private sealed class RecordingFFmpegService(FakeRemux remux, IServiceProvider provider) : FFmpegService(
            NullLogger<FFmpegService>.Instance, provider.GetRequiredService<IConfiguration>(), new ClipMediaUrls(provider))
        {
            public override async Task<DownloadedVideo> DownloadHlsVideoAsync(ClipVideoRef video,
                CancellationToken cancellationToken = default)
            {
                lock (remux.Downloaded)
                {
                    remux.Downloaded.Add(video);
                }

                string path = Path.GetTempFileName();
                await File.WriteAllTextAsync(path, Content, cancellationToken);
                return new DownloadedVideo(path, new SemaphoreSlim(0, 1));
            }
        }
    }

    private sealed class TestUserScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Site";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
