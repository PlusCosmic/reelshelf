using System.Text.RegularExpressions;

namespace Reelshelf.Games;

/// <summary>The three images an IGDB category carries.</summary>
public enum GameArtKind
{
    Cover,
    KeyArt,
    Logo
}

/// <summary>
/// Where our copy of a category's IGDB image lives in the R2 bucket:
/// <c>game-art/{categoryId}/{kind}/{size}/{imageId}.{ext}</c>, from the IGDB URL it copies
/// (<c>https://images.igdb.com/igdb/image/upload/t_{size}/{imageId}.{ext}</c>). IGDB gives a new image a new id, so a
/// key's content never changes and the copy is cached <c>immutable</c>; a changed image is a new key and a new URL.
/// </summary>
public static partial class GameArtKeys
{
    public const string Prefix = "game-art/";

    /// <summary>The key for <paramref name="sourceUrl"/>'s copy, or null when it isn't an IGDB image we copy.</summary>
    public static string? For(Guid categoryId, GameArtKind kind, string? sourceUrl)
    {
        if (sourceUrl is null || !GameCoverColors.CanFetch(sourceUrl)) return null;

        Match match = IgdbImagePath().Match(new Uri(sourceUrl).AbsolutePath);
        if (!match.Success) return null;

        return $"{Prefix}{categoryId}/{KindSegment(kind)}/{match.Groups["size"].Value}/{match.Groups["file"].Value}";
    }

    /// <summary>The <c>Content-Type</c> a copy is stored with.</summary>
    public static string ContentType(string key) => Path.GetExtension(key) switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a game art key")
    };

    private static string KindSegment(GameArtKind kind) => kind switch
    {
        GameArtKind.Cover => "cover",
        GameArtKind.KeyArt => "key-art",
        GameArtKind.Logo => "logo",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    [GeneratedRegex(@"^/igdb/image/upload/(?<size>t_[a-z0-9_]+)/(?<file>[a-z0-9]+\.(?:jpg|png|webp))$")]
    private static partial Regex IgdbImagePath();
}

/// <summary>
/// The image URLs a category is served with. Each is our copy on the media domain (<c>R2:MediaBaseUrl</c>) when we
/// hold a copy of the current source, and the source otherwise: without R2, before the first copy, or while a changed
/// image waits to be copied. So a URL never points at a file that isn't there.
/// </summary>
public sealed class GameArtUrls(string? mediaBaseUrl)
{
    private readonly string? _mediaBaseUrl = string.IsNullOrWhiteSpace(mediaBaseUrl) ? null : mediaBaseUrl.TrimEnd('/');

    public string? Serve(Guid categoryId, GameArtKind kind, string? sourceUrl, string? storedKey)
    {
        if (_mediaBaseUrl is null || storedKey is null) return sourceUrl;
        return storedKey == GameArtKeys.For(categoryId, kind, sourceUrl) ? $"{_mediaBaseUrl}/{storedKey}" : sourceUrl;
    }

    public string? Cover(GameCategory category) =>
        Serve(category.Id, GameArtKind.Cover, category.CoverUrl, category.CoverKey);

    public string? KeyArt(GameCategory category) =>
        Serve(category.Id, GameArtKind.KeyArt, category.KeyArtUrl, category.KeyArtKey);

    public string? Logo(GameCategory category) =>
        Serve(category.Id, GameArtKind.Logo, category.GameLogoUrl, category.GameLogoKey);
}
