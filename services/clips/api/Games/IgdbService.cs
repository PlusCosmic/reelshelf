using IGDB;
using IGDB.Models;
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
        var games = await _client.QueryAsync<Game>(
            IGDBClient.Endpoints.Games,
            $"where id = {igdbId}; fields name,slug,cover.url,genres.name,platforms.name,summary;"
        );

        var game = games.FirstOrDefault();
        if (game == null) return null;

        return new GameDetails(
            game.Id ?? 0,
            game.Name ?? "",
            game.Slug ?? "",
            game.Cover?.Value?.Url != null
                ? ConvertToHighResCover(game.Cover.Value.Url)
                : null,
            game.Genres?.Values.Select(g => g.Name ?? "").ToList() ?? [],
            game.Platforms?.Values.Select(p => p.Name ?? "").ToList() ?? [],
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

    private static string EscapeQuery(string query)
    {
        return query.Replace("\"", "\\\"");
    }
}
