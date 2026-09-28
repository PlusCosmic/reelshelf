namespace Reelshelf.Games;

/// <summary>
/// Works out <see cref="ClothColor"/> for a category's cover. Only IGDB covers are fetched: custom categories carry
/// whatever URL their creator typed, and the server must not request arbitrary addresses on a user's behalf.
/// </summary>
public class GameCoverColors(IHttpClientFactory httpClientFactory, ILogger<GameCoverColors> logger)
{
    public const string HttpClientName = "game-cover-colors";

    private const string IgdbImageHost = "images.igdb.com";

    // A t_cover_big JPEG is well under 100 KB; anything this large is not a cover.
    private const long MaxCoverBytes = 5 * 1024 * 1024;

    public static bool CanFetch(string? coverUrl) =>
        Uri.TryCreate(coverUrl, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, IgdbImageHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>The cloth colour for <paramref name="coverUrl"/>, or null when it cannot be fetched or read.</summary>
    public async Task<string?> TryGetClothColorAsync(string? coverUrl, CancellationToken cancellationToken)
    {
        if (!CanFetch(coverUrl)) return null;

        try
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response =
                await client.GetAsync(coverUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxCoverBytes) return null;

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxCoverBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            return await ClothColor.FromImageAsync(buffer.ToArray(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not work out a cloth colour from cover {CoverUrl}", coverUrl);
            return null;
        }
    }
}
