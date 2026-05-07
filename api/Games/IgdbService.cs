using IGDB;
using IGDB.Models;
using Newtonsoft.Json;
using Reelshelf.Shared.Games;

namespace Reelshelf.Games;

public class IgdbService
{
    private readonly IGDBClient _client;

    public IgdbService(IConfiguration configuration)
    {
        var clientId = configuration["IgdbClientId"]
            ?? throw new InvalidOperationException("IgdbClientId not configured");
        var clientSecret = configuration["IgdbClientSecret"]
            ?? throw new InvalidOperationException("IgdbClientSecret not configured");

        _client = new IGDBClient(clientId, clientSecret);
    }

    public async Task<List<GameSearchResult>> SearchGamesAsync(string query, int limit = 10)
    {
        var games = await _client.QueryAsync<Game>(
            IGDBClient.Endpoints.Games,
            $"search \"{EscapeQuery(query)}\"; fields name,slug,cover.url; limit {limit};"
        );

        return games.Select(g => new GameSearchResult(
            g.Id ?? 0,
            g.Name ?? "",
            g.Slug ?? "",
            g.Cover?.Value?.Url != null
                ? ConvertToHighResCover(g.Cover.Value.Url)
                : null
        )).ToList();
    }

    public async Task<GameDetails?> GetGameByIdAsync(long igdbId)
    {
        var games = await _client.QueryAsync<IgdbGameDetails>(
            IGDBClient.Endpoints.Games,
            $"""
            where id = {igdbId};
            fields name,slug,cover.image_id,cover.url,genres.name,platforms.name,summary;
            """
        );

        var game = games.FirstOrDefault();
        if (game == null) return null;

        var gameId = game.Id ?? igdbId;
        var artworkAssets = SelectArtworkAssets(
            await GetArtworksForGameAsync(gameId),
            await GetScreenshotsForGameAsync(gameId));

        return new GameDetails(
            game.Id ?? 0,
            game.Name ?? "",
            game.Slug ?? "",
            GetCoverUrl(game.Cover),
            artworkAssets.KeyArtUrl,
            artworkAssets.GameLogoUrl,
            game.Genres?.Select(g => g.Name ?? "").ToList() ?? [],
            game.Platforms?.Select(p => p.Name ?? "").ToList() ?? [],
            game.Summary
        );
    }

    private async Task<List<IgdbArtwork>> GetArtworksForGameAsync(long igdbId)
    {
        var artworks = (await _client.QueryAsync<IgdbArtwork>(
            "artworks",
            $"""
            where game = {igdbId};
            fields image_id,url,width,height,alpha_channel,artwork_type;
            limit 50;
            """
        )).ToList();

        var artworkTypeIds = artworks
            .Select(a => a.ArtworkTypeId)
            .OfType<long>()
            .Distinct()
            .ToList();

        if (artworkTypeIds.Count == 0) return artworks;

        var artworkTypes = (await _client.QueryAsync<IgdbNamedEntity>(
            "artwork_types",
            $"""
            where id = ({string.Join(",", artworkTypeIds)});
            fields id,name,slug;
            limit {artworkTypeIds.Count};
            """
        )).ToDictionary(t => t.Id);

        foreach (var artwork in artworks)
        {
            if (artwork.ArtworkTypeId is { } typeId && artworkTypes.TryGetValue(typeId, out var artworkType))
            {
                artwork.ArtworkType = artworkType;
            }
        }

        return artworks;
    }

    private async Task<List<IgdbImage>> GetScreenshotsForGameAsync(long igdbId)
    {
        return (await _client.QueryAsync<IgdbImage>(
            "screenshots",
            $"""
            where game = {igdbId};
            fields image_id,url,width,height;
            limit 50;
            """
        )).ToList();
    }

    private static string? GetCoverUrl(IgdbImage? cover)
    {
        if (cover == null) return null;

        return !string.IsNullOrWhiteSpace(cover.ImageId)
            ? BuildIgdbImageUrl(cover.ImageId, "cover_big")
            : cover.Url != null
                ? ConvertToHighResCover(cover.Url)
                : null;
    }

    private static string ConvertToHighResCover(string url)
    {
        // IGDB returns thumbnail URLs, convert to high-res
        // //images.igdb.com/igdb/image/upload/t_thumb/co1wj6.jpg
        // -> https://images.igdb.com/igdb/image/upload/t_cover_big/co1wj6.jpg
        return "https:" + url.Replace("t_thumb", "t_cover_big");
    }

    private static (string? KeyArtUrl, string? GameLogoUrl) SelectArtworkAssets(
        List<IgdbArtwork> artworks,
        List<IgdbImage> screenshots)
    {
        var orderedArtwork = artworks
            .Where(a => GetImageId(a) != null)
            .OrderByDescending(ImageScore)
            .ToList();

        var gameLogo = orderedArtwork.FirstOrDefault(IsGameLogo);
        var keyArt = orderedArtwork.FirstOrDefault(IsKeyArtWithoutLogo)
            ?? orderedArtwork.FirstOrDefault(a => !IsGameLogo(a));
        var screenshot = screenshots
            .Where(s => GetImageId(s) != null)
            .OrderByDescending(ImageScore)
            .FirstOrDefault();

        return (
            keyArt != null
                ? BuildIgdbImageUrl(GetImageId(keyArt)!, "1080p")
                : screenshot != null
                    ? BuildIgdbImageUrl(GetImageId(screenshot)!, "1080p")
                    : null,
            gameLogo != null ? BuildIgdbImageUrl(GetImageId(gameLogo)!, "1080p", "png") : null
        );
    }

    private static int ImageScore(IgdbImage image)
    {
        var width = image.Width ?? 0;
        var height = image.Height ?? 0;
        var landscapeBonus = width > height ? 1_000_000 : 0;
        return landscapeBonus + width * height;
    }

    private static bool IsGameLogo(IgdbArtwork artwork) =>
        NormalizedArtworkTypeValues(artwork).Any(value => value.Contains("logo"));

    private static bool IsKeyArtWithoutLogo(IgdbArtwork artwork) =>
        MatchesArtworkType(artwork, "key art without logo") || MatchesArtworkType(artwork, "key-art-without-logo");

    private static bool MatchesArtworkType(IgdbArtwork artwork, string expected)
    {
        var normalizedExpected = NormalizeArtworkType(expected);
        return NormalizeArtworkType(artwork.ArtworkType?.Name) == normalizedExpected
            || NormalizeArtworkType(artwork.ArtworkType?.Slug) == normalizedExpected;
    }

    private static string NormalizeArtworkType(string? value) =>
        (value ?? "").Replace("-", " ").Replace("_", " ").Trim().ToLowerInvariant();

    private static IEnumerable<string> NormalizedArtworkTypeValues(IgdbArtwork artwork)
    {
        yield return NormalizeArtworkType(artwork.ArtworkType?.Name);
        yield return NormalizeArtworkType(artwork.ArtworkType?.Slug);
    }

    private static string? GetImageId(IgdbImage image) =>
        !string.IsNullOrWhiteSpace(image.ImageId)
            ? image.ImageId
            : image.Url?.Split('/').LastOrDefault()?.Split('.').FirstOrDefault();

    private static string BuildIgdbImageUrl(string imageId, string size, string extension = "jpg") =>
        $"https://images.igdb.com/igdb/image/upload/t_{size}/{imageId}.{extension}";

    private static string EscapeQuery(string query)
    {
        return query.Replace("\"", "\\\"");
    }

    private sealed class IgdbGameDetails
    {
        [JsonProperty("id")]
        public long? Id { get; init; }

        [JsonProperty("name")]
        public string? Name { get; init; }

        [JsonProperty("slug")]
        public string? Slug { get; init; }

        [JsonProperty("cover")]
        public IgdbImage? Cover { get; init; }

        [JsonProperty("genres")]
        public List<IgdbNamedEntity>? Genres { get; init; }

        [JsonProperty("platforms")]
        public List<IgdbNamedEntity>? Platforms { get; init; }

        [JsonProperty("summary")]
        public string? Summary { get; init; }
    }

    private sealed class IgdbArtwork : IgdbImage
    {
        [JsonProperty("artwork_type")]
        public long? ArtworkTypeId { get; init; }

        [JsonIgnore]
        public IgdbNamedEntity? ArtworkType { get; set; }
    }

    private class IgdbImage
    {
        [JsonProperty("image_id")]
        public string? ImageId { get; init; }

        [JsonProperty("url")]
        public string? Url { get; init; }

        [JsonProperty("width")]
        public int? Width { get; init; }

        [JsonProperty("height")]
        public int? Height { get; init; }
    }

    private sealed class IgdbNamedEntity
    {
        [JsonProperty("id")]
        public long Id { get; init; }

        [JsonProperty("name")]
        public string? Name { get; init; }

        [JsonProperty("slug")]
        public string? Slug { get; init; }
    }
}
