using Reelshelf.Games;
using Xunit;

namespace Reelshelf.Test.Games;

public class GameArtTests
{
    private static readonly Guid Category = Guid.Parse("6f1c1f9e-4b9a-4a59-9a43-0d1c8f1e2a3b");
    private const string Cover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1wj6.jpg";
    private const string Logo = "https://images.igdb.com/igdb/image/upload/t_1080p/ar2x9z.png";

    [Fact]
    public void Keys_AreNamedAfterIgdbsImage()
    {
        Assert.Equal($"game-art/{Category}/cover/t_cover_big/co1wj6.jpg",
            GameArtKeys.For(Category, GameArtKind.Cover, Cover));
        Assert.Equal($"game-art/{Category}/logo/t_1080p/ar2x9z.png", GameArtKeys.For(Category, GameArtKind.Logo, Logo));
        Assert.Equal("image/png", GameArtKeys.ContentType(GameArtKeys.For(Category, GameArtKind.Logo, Logo)!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/igdb/image/upload/t_cover_big/co1wj6.jpg")]
    [InlineData("http://images.igdb.com/igdb/image/upload/t_cover_big/co1wj6.jpg")]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/../co1wj6.jpg")]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co1wj6.gif")]
    [InlineData("https://images.igdb.com/somewhere/else.jpg")]
    public void OnlyIgdbImages_GetAKey(string? source)
    {
        Assert.Null(GameArtKeys.For(Category, GameArtKind.Cover, source));
    }

    [Fact]
    public void TheCopy_IsServed_OnlyWhileItCopiesTheCurrentSource()
    {
        GameArtUrls urls = new("https://media.reelshelf.app/");
        string key = GameArtKeys.For(Category, GameArtKind.Cover, Cover)!;

        Assert.Equal($"https://media.reelshelf.app/{key}", urls.Serve(Category, GameArtKind.Cover, Cover, key));
        // Not copied yet.
        Assert.Equal(Cover, urls.Serve(Category, GameArtKind.Cover, Cover, null));
        // IGDB's cover changed and the new one isn't copied yet: the old copy isn't the cover any more.
        const string newCover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co9zzz.jpg";
        Assert.Equal(newCover, urls.Serve(Category, GameArtKind.Cover, newCover, key));
        // A custom category's own URL is served as it is.
        Assert.Equal("https://example.com/c.png", urls.Serve(Category, GameArtKind.Cover, "https://example.com/c.png", null));
        Assert.Null(urls.Serve(Category, GameArtKind.KeyArt, null, null));
    }

    [Fact]
    public void WithoutR2_TheSourceIsServed()
    {
        GameArtUrls urls = new(null);
        string key = GameArtKeys.For(Category, GameArtKind.Cover, Cover)!;
        Assert.Equal(Cover, urls.Serve(Category, GameArtKind.Cover, Cover, key));
    }
}
