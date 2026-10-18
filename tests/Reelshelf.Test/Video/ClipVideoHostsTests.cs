using Dapper;
using Microsoft.Extensions.Configuration;
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
    private static readonly Guid Listed = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Unlisted = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void For_RoutesByTheClipsVideoHost()
    {
        FakeHost bunny = new();
        FakeHost r2 = new();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, bunny);
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, r2);
        ClipVideoHosts hosts = new(services.BuildServiceProvider());

        Assert.Same(bunny, hosts.For(VideoHostNames.Bunny));
        Assert.Same(r2, hosts.For(VideoHostNames.R2));
    }

    [Fact]
    public void For_AnUnregisteredHost_Throws()
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, new FakeHost());
        ClipVideoHosts hosts = new(services.BuildServiceProvider());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => hosts.For(VideoHostNames.R2));
        Assert.Contains("'r2'", error.Message);
    }

    [Fact]
    public void AddClipVideoHost_RegistersTheHostAndItsMediaUrlsUnderItsName()
    {
        ServiceCollection services = new();
        services.AddClipVideoHost<FakeHost, FakeMediaUrls>(VideoHostNames.R2);
        services.AddSingleton<ClipMediaUrls>();
        services.AddClipVideoHostRouting(Configuration(("VideoHost:NewUploads", "r2")));
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
    }

    [Fact]
    public void Policy_ByDefault_SendsEveryoneToBunny()
    {
        NewUploadVideoHostPolicy policy = NewUploadVideoHostPolicy.FromConfiguration(Configuration());

        Assert.Equal(VideoHostNames.Bunny, policy.NewUploads);
        Assert.Equal(VideoHostNames.Bunny, policy.HostFor(Listed));
        Assert.Equal(VideoHostNames.Bunny, policy.HostFor(Unlisted));
    }

    [Fact]
    public void Policy_SendsOnlyListedUsersToTheConfiguredHost()
    {
        NewUploadVideoHostPolicy policy = NewUploadVideoHostPolicy.FromConfiguration(Configuration(
            ("VideoHost:NewUploads", "r2"),
            ("VideoHost:Users:0", Listed.ToString())));

        Assert.Equal(VideoHostNames.R2, policy.HostFor(Listed));
        Assert.Equal(VideoHostNames.Bunny, policy.HostFor(Unlisted));
    }

    [Fact]
    public void Policy_WithNoUsersListed_SendsEveryoneToBunny()
    {
        NewUploadVideoHostPolicy policy = NewUploadVideoHostPolicy.FromConfiguration(Configuration(
            ("VideoHost:NewUploads", "R2")));

        Assert.Equal(VideoHostNames.R2, policy.NewUploads);
        Assert.Equal(VideoHostNames.Bunny, policy.HostFor(Listed));
    }

    [Theory]
    [InlineData("VideoHost:NewUploads", "s3")]
    [InlineData("VideoHost:Users:0", "harry")]
    public void Policy_RefusesBadConfiguration(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            NewUploadVideoHostPolicy.FromConfiguration(Configuration((key, value))));
    }

    [Fact]
    public void Routing_FailsAtStartupWhenNewUploadsNamesAnUnregisteredHost()
    {
        ServiceCollection services = new();
        services.AddClipVideoHost<FakeHost, FakeMediaUrls>(VideoHostNames.Bunny);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            services.AddClipVideoHostRouting(Configuration(
                ("VideoHost:NewUploads", "r2"),
                ("VideoHost:Users:0", Listed.ToString()))));
        Assert.Contains("'r2'", error.Message);
    }

    [Fact]
    public void Routing_WithTheDefaultConfiguration_NeedsOnlyBunny()
    {
        ServiceCollection services = new();
        services.AddClipVideoHost<FakeHost, FakeMediaUrls>(VideoHostNames.Bunny);

        services.AddClipVideoHostRouting(Configuration());

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(VideoHostNames.Bunny, provider.GetRequiredService<NewUploadVideoHostPolicy>().HostFor(Listed));
    }

    /// <summary>
    /// Runs V39 against a temporary <c>clip</c> table: existing rows become Bunny clips, and the check constraint
    /// takes exactly the host names the API knows.
    /// </summary>
    [PostgresFact]
    public async Task Migration_PutsExistingClipsOnBunny_AndAllowsOnlyKnownHosts()
    {
        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(PostgresFactAttribute.ConnectionString!);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TEMP TABLE clip (id serial PRIMARY KEY)");
        await connection.ExecuteAsync("INSERT INTO clip DEFAULT VALUES");

        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V39__clip_video_host.sql")));

        Assert.Equal(["bunny"], await connection.QueryAsync<string>("SELECT video_host FROM clip"));
        foreach (string host in VideoHostNames.All)
        {
            await connection.ExecuteAsync("INSERT INTO clip (video_host) VALUES (@host)", new { host });
        }

        await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("INSERT INTO clip (video_host) VALUES ('s3')"));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
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

        public Task DeleteContainerAsync(Guid containerId) => Task.CompletedTask;
    }
}
