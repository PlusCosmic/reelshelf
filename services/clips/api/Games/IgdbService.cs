using IGDB;
using IGDB.Models;
using Newtonsoft.Json;
using Nucleus.Shared.Games;

namespace Nucleus.Clips.Games;

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
            fields name,slug,cover.url,genres.name,platforms.name,summary,
                artworks.image_id,artworks.url,artworks.width,artworks.height,artworks.alpha_channel,
                artworks.artwork_type.name,artworks.artwork_type.slug;
            """
        );

        var game = games.FirstOrDefault();
        if (game == null) return null;

        var artworkAssets = SelectArtworkAssets(game.Artworks ?? []);

        return new GameDetails(
            game.Id ?? 0,
            game.Name ?? "",
            game.Slug ?? "",
            game.Cover?.Url != null
                ? ConvertToHighResCover(game.Cover.Url)
                : null,
            artworkAssets.KeyArtUrl,
            artworkAssets.GameLogoUrl,
            game.Genres?.Select(g => g.Name ?? "").ToList() ?? [],
            game.Platforms?.Select(p => p.Name ?? "").ToList() ?? [],
            game.Summary
        );
    }

    private static string ConvertToHighResCover(string url)
    {
        // IGDB returns thumbnail URLs, convert to high-res
        // //images.igdb.com/igdb/image/upload/t_thumb/co1wj6.jpg
        // -> https://images.igdb.com/igdb/image/upload/t_cover_big/co1wj6.jpg
        return "https:" + url.Replace("t_thumb", "t_cover_big");
    }

    private static (string? KeyArtUrl, string? GameLogoUrl) SelectArtworkAssets(List<IgdbArtwork> artworks)
    {
        var orderedArtwork = artworks
            .Where(a => GetImageId(a) != null)
            .OrderByDescending(a => ArtworkScore(a))
            .ToList();

        var gameLogo = orderedArtwork.FirstOrDefault(IsGameLogo);
        var keyArt = orderedArtwork.FirstOrDefault(IsKeyArtWithoutLogo)
            ?? orderedArtwork.FirstOrDefault(a => !IsGameLogo(a));

        return (
            keyArt != null ? BuildIgdbImageUrl(GetImageId(keyArt)!, "1080p") : null,
            gameLogo != null ? BuildIgdbImageUrl(GetImageId(gameLogo)!, "1080p", "png") : null
        );
    }

    private static int ArtworkScore(IgdbArtwork artwork)
    {
        var width = artwork.Width ?? 0;
        var height = artwork.Height ?? 0;
        var landscapeBonus = width > height ? 1_000_000 : 0;
        return landscapeBonus + width * height;
    }

    private static bool IsGameLogo(IgdbArtwork artwork) =>
        MatchesArtworkType(artwork, "game logo") || MatchesArtworkType(artwork, "game-logo");

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

    private static string? GetImageId(IgdbArtwork artwork) =>
        !string.IsNullOrWhiteSpace(artwork.ImageId)
            ? artwork.ImageId
            : artwork.Url?.Split('/').LastOrDefault()?.Split('.').FirstOrDefault();

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

        [JsonProperty("artworks")]
        public List<IgdbArtwork>? Artworks { get; init; }
    }

    private sealed class IgdbArtwork : IgdbImage
    {
        [JsonProperty("artwork_type")]
        public IgdbNamedEntity? ArtworkType { get; init; }
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
        [JsonProperty("name")]
        public string? Name { get; init; }

        [JsonProperty("slug")]
        public string? Slug { get; init; }
    }
}
