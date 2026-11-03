using Reelshelf.Video.R2;

namespace Reelshelf.Games;

/// <summary>Where copies of game art are kept: the R2 bucket, or nothing when R2 isn't configured.</summary>
public interface IGameArtStore
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string key);
}

public sealed class R2GameArtStore(R2Storage storage) : IGameArtStore
{
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken) =>
        storage.PutAsync(key, content, contentType, R2ObjectKeys.ImmutableCacheControl, cancellationToken);

    public Task DeleteAsync(string key) => storage.DeleteObjectsAsync([key]);
}

/// <summary>
/// Copies a category's IGDB images into our own storage, which IGDB's API terms allow and prefer, so the site serves
/// them from the media domain (<see cref="GameArtUrls"/>) and IGDB's image CDN isn't asked on every page view. An
/// image is copied once per IGDB image id (<see cref="GameArtKeys"/>); when IGDB's image changes, the new one is copied
/// and the old copy deleted. Only <c>images.igdb.com</c> URLs are fetched, never a custom category's.
/// </summary>
public sealed class GameArtMirror(
    IGameArtStore? store,
    GameCategoryStatements statements,
    IHttpClientFactory httpClientFactory,
    ILogger<GameArtMirror> logger)
{
    public const string HttpClientName = "game-art";

    // A 1080p PNG logo is a few MB at most.
    private const long MaxImageBytes = 20 * 1024 * 1024;

    public bool IsConfigured => store is not null;

    /// <summary>Copies whichever of the category's images has no copy of its current source. False if any failed.</summary>
    public async Task<bool> CopyAsync(GameCategory category, CancellationToken cancellationToken)
    {
        if (store is null) return true;

        bool ok = true;
        ok &= await CopyAsync(category.Id, GameArtKind.Cover, category.CoverUrl, category.CoverKey, cancellationToken);
        ok &= await CopyAsync(category.Id, GameArtKind.KeyArt, category.KeyArtUrl, category.KeyArtKey, cancellationToken);
        ok &= await CopyAsync(category.Id, GameArtKind.Logo, category.GameLogoUrl, category.GameLogoKey, cancellationToken);
        return ok;
    }

    private async Task<bool> CopyAsync(Guid categoryId, GameArtKind kind, string? sourceUrl, string? storedKey,
        CancellationToken cancellationToken)
    {
        string? key = GameArtKeys.For(categoryId, kind, sourceUrl);
        if (key is null || key == storedKey) return true;

        try
        {
            byte[]? image = await DownloadAsync(sourceUrl!, cancellationToken);
            if (image is null) return false;

            await store!.PutAsync(key, image, GameArtKeys.ContentType(key), cancellationToken);
            (bool recorded, string? replaced) = await statements.SetArtKeyAsync(categoryId, kind, sourceUrl!, key);
            if (!recorded)
            {
                // The image changed while it was being copied; the next pass copies the new one.
                await store.DeleteAsync(key);
                return true;
            }

            if (replaced is not null && replaced.StartsWith(GameArtKeys.Prefix, StringComparison.Ordinal))
            {
                await store.DeleteAsync(replaced);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not copy the {Kind} of game category {CategoryId} from {SourceUrl}",
                kind, categoryId, sourceUrl);
            return false;
        }
    }

    private async Task<byte[]?> DownloadAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response =
            await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength > MaxImageBytes)
        {
            logger.LogWarning("{SourceUrl} is not an image we copy ({MediaType}, {Length} bytes)",
                sourceUrl, mediaType, response.Content.Headers.ContentLength);
            return null;
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }
}
