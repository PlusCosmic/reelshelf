using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Games;
using Reelshelf.LandingShelf;
using Xunit;

namespace Reelshelf.Test.Games;

public class LandingShelfTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_KeepsIgdbsOrder_AndAsksForTheConfiguredRanking()
    {
        FakeSource source = new([Game(1, "Counter-Strike 2"), Game(2, "Dota 2"), Game(3, "PUBG: Battlegrounds")]);
        LandingShelfCache cache = new();

        bool refreshed = await Refresher(source, cache, new LandingShelfOptions { PopularityType = 1, GameCount = 12 })
            .RefreshAsync(CancellationToken.None);

        Assert.True(refreshed);
        Assert.Equal((1, 12), source.LastRequest);
        Assert.Equal(["Counter-Strike 2", "Dota 2", "PUBG: Battlegrounds"], cache.Current.Games.Select(g => g.Name));
        Assert.Equal(Now, cache.Current.UpdatedAt);
    }

    [Fact]
    public async Task Refresh_LeavesTheClothEmpty_WhenACoverCannotBeRead()
    {
        LandingShelfCache cache = new();

        await Refresher(new FakeSource([Game(7, "Rust")]), cache).RefreshAsync(CancellationToken.None);

        LandingShelfGame rust = Assert.Single(cache.Current.Games);
        Assert.Null(rust.ClothColor);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co7.jpg", rust.CoverUrl);
    }

    [Fact]
    public async Task Refresh_KeepsThePreviousShelf_WhenIgdbReturnsNothing()
    {
        LandingShelfCache cache = new();
        LandingShelfResponse previous = new([new LandingShelfGame(1, "Minecraft", "minecraft", "https://x", "#5a9148")], Now);
        cache.Set(previous);

        bool refreshed = await Refresher(new FakeSource([]), cache).RefreshAsync(CancellationToken.None);

        Assert.False(refreshed);
        Assert.Same(previous, cache.Current);
    }

    [Fact]
    public void Cache_StartsEmpty_SoThePageKeepsItsBuiltInShelf()
    {
        LandingShelfResponse shelf = new LandingShelfCache().Current;

        Assert.Empty(shelf.Games);
        Assert.Null(shelf.UpdatedAt);
    }

    private static PopularGame Game(long id, string name) =>
        new(id, name, name.ToLowerInvariant().Replace(' ', '-'),
            $"https://images.igdb.com/igdb/image/upload/t_cover_big/co{id}.jpg");

    private static LandingShelfRefresher Refresher(
        IPopularGameSource source, LandingShelfCache cache, LandingShelfOptions? options = null) =>
        new(
            source,
            new GameCoverColors(new NotFoundClients(), NullLogger<GameCoverColors>.Instance),
            cache,
            Options.Create(options ?? new LandingShelfOptions()),
            new FixedTime(Now),
            NullLogger<LandingShelfRefresher>.Instance);

    private sealed class FakeSource(IReadOnlyList<PopularGame> games) : IPopularGameSource
    {
        public (int PopularityType, int Count)? LastRequest { get; private set; }

        public Task<IReadOnlyList<PopularGame>> GetPopularGamesAsync(int popularityType, int count)
        {
            LastRequest = (popularityType, count);
            return Task.FromResult(games);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Every cover request gets a 404, so no test fetches an image or runs ffmpeg.
    private sealed class NotFoundClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NotFound());

        private sealed class NotFound : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
