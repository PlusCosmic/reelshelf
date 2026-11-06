using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Games;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Video;

public class ClipVideoHostsTests
{
    [Fact]
    public void For_RoutesByTheClipsVideoHost()
    {
        FakeHost r2 = new();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, r2);
        ClipVideoHosts hosts = new(services.BuildServiceProvider());

        Assert.Same(r2, hosts.For(VideoHostNames.R2));
    }

    [Fact]
    public void For_AnUnregisteredHost_Throws()
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, new FakeHost());
        ClipVideoHosts hosts = new(services.BuildServiceProvider());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => hosts.For("bunny"));
        Assert.Contains("'bunny'", error.Message);
    }

    [Fact]
    public void AddClipVideoHost_RegistersTheHostAndItsMediaUrlsUnderItsName()
    {
        ServiceCollection services = new();
        services.AddClipVideoHost<FakeHost, FakeMediaUrls>(VideoHostNames.R2);
        services.AddSingleton<ClipMediaUrls>();
        services.AddClipVideoHostRouting(requireR2: true);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<FakeHost>(scope.ServiceProvider.GetRequiredService<ClipVideoHosts>().For(VideoHostNames.R2));
        Assert.Equal("fake", provider.GetRequiredService<ClipMediaUrls>().Thumbnail(new ClipVideoRef(VideoHostNames.R2, Guid.Empty)));
    }

    [Fact]
    public void AddClipVideoHost_RefusesANameTheColumnDoesNotAllow()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() => services.AddClipVideoHost<FakeHost, FakeMediaUrls>("s3"));
        Assert.Throws<ArgumentException>(() => services.AddClipVideoHost<FakeHost, FakeMediaUrls>("bunny"));
    }

    [Fact]
    public void Routing_FailsAtStartupWithoutR2()
    {
        ServiceCollection services = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            services.AddClipVideoHostRouting(requireR2: true));
        Assert.Contains("R2:", error.Message);
    }

    [Fact]
    public void Routing_WithoutR2_IsAllowedWhenNotRequired()
    {
        ServiceCollection services = new();

        services.AddClipVideoHostRouting(requireR2: false);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ClipVideoHosts>());
    }

    /// <summary>
    /// Runs V39 and then V62 against temporary <c>clip</c> and <c>transcode_run</c> tables: V62 refuses while a clip is
    /// still on Bunny, and afterwards the check constraint takes exactly the host names the API knows and the
    /// <c>migrate</c> jobs are gone.
    /// </summary>
    [PostgresFact]
    public async Task Migration_RefusesWhileAClipIsOnBunny_ThenAllowsOnlyKnownHosts()
    {
        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(PostgresFactAttribute.ConnectionString!);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            CREATE TEMP TABLE clip (id serial PRIMARY KEY);
            CREATE TEMP TABLE transcode_run (id serial PRIMARY KEY, type text NOT NULL);
            INSERT INTO clip DEFAULT VALUES;
            INSERT INTO transcode_run (type) VALUES ('transcode'), ('migrate');
            """);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V39__clip_video_host.sql")));
        string removeBunny = await File.ReadAllTextAsync(MigrationPath("V62__bunny_removed.sql"));

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(removeBunny));
        Assert.Contains("still on Bunny", refused.MessageText);

        await connection.ExecuteAsync("DELETE FROM clip");
        await connection.ExecuteAsync(removeBunny);

        Assert.Equal(["transcode"], await connection.QueryAsync<string>("SELECT type FROM transcode_run"));
        foreach (string host in VideoHostNames.All)
        {
            await connection.ExecuteAsync("INSERT INTO clip (video_host) VALUES (@host)", new { host });
        }

        Assert.Equal("r2", await connection.QuerySingleAsync<string>("INSERT INTO clip DEFAULT VALUES RETURNING video_host"));
        await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("INSERT INTO clip (video_host) VALUES ('bunny')"));
        await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("INSERT INTO transcode_run (type) VALUES ('migrate')"));
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

    private sealed class FakeMediaUrls : IClipVideoMediaUrls
    {
        public List<PlaybackSource> Sources(ClipVideoRef video) => [];

        public Uri ServerStream(ClipVideoRef video) => new("https://fake.example/playlist.m3u8");

        public string? Thumbnail(ClipVideoRef video) => "fake";

        public List<string> DetectionFrames(ClipVideoRef video) => [];

        public string MediaPath(ClipVideoRef video) => "fake";
    }

    private sealed class FakeHost : IClipVideoHost
    {
        public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title) =>
            throw new NotSupportedException();

        public Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize) =>
            throw new NotSupportedException();

        public Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HostedVideoInfo?> GetVideoAsync(Guid videoId) => throw new NotSupportedException();

        public Task DeleteVideoAsync(Guid videoId) => throw new NotSupportedException();
    }
}
