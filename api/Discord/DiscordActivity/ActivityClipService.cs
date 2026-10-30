using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Video;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// One of a room member's own clips, as the Activity's picker shows it. The Activity builds the thumbnail URL from
/// <paramref name="Media"/> through its URL Mapping for the clip's host.
/// </summary>
public sealed record ActivityClip(
    Guid ClipId,
    Guid VideoId,
    string Title,
    string Game,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    bool Ready,
    ActivityMedia Media);

public sealed record ActivityClipsResponse(List<ActivityClip> Clips, bool HasMore);

/// <summary>
/// A game on a room member's shelf, as the picker's spine for it shows it: its cloth colour (null for a custom
/// game, which the Activity colours itself) and how many clips the member has in it.
/// </summary>
public sealed record ActivityGame(Guid Id, string Name, string? ClothColor, int ClipCount);

public sealed record ActivityGamesResponse(List<ActivityGame> Games);

/// <summary>Lists a room member's own clips for the Activity, newest first unless asked otherwise, and the games they are in.</summary>
public sealed class ActivityClipService(
    ClipsStatements clips,
    GameCategoryStatements gameCategories,
    ClipMediaUrls mediaUrls)
{
    public const int PageSize = 24;

    public async Task<ActivityClipsResponse> GetOwnClips(
        Guid accountId,
        string? search,
        int page,
        Guid? categoryId = null,
        ClipSortOrder sortOrder = ClipSortOrder.DateDescending)
    {
        page = Math.Max(page, 1);
        ClipsStatements.PagedClipWithTagsRows rows = await clips.GetClipsWithTags(
            accountId,
            gameCategoryId: categoryId,
            search: search,
            sortOrder: sortOrder,
            limit: PageSize,
            offset: (page - 1) * PageSize);

        Dictionary<Guid, string> gameNames = (await gameCategories.GetByIdsAsync(
                rows.Rows.Select(row => row.GameCategoryId).Distinct().ToList()))
            .ToDictionary(category => category.Id, category => category.Name);

        List<ActivityClip> items = rows.Rows.Select(row => new ActivityClip(
                row.Id,
                row.VideoId,
                string.IsNullOrWhiteSpace(row.Title) ? "Untitled clip" : row.Title,
                gameNames.GetValueOrDefault(row.GameCategoryId, ""),
                row.Length ?? 0,
                row.CreatedAt,
                row.VideoStatus.IsPlayable(),
                ActivityMedia.For(mediaUrls, row.ToVideoRef())))
            .ToList();

        return new ActivityClipsResponse(items, page * PageSize < rows.TotalCount);
    }

    /// <summary>
    /// Every game the member has clips in, most clips first, so the picker's spines stay in one order while
    /// the member switches between them. Counts cover every clip, as the unfiltered list does.
    /// </summary>
    public async Task<ActivityGamesResponse> GetOwnGames(Guid accountId)
    {
        List<ClipsStatements.CategoryTotalsRow> totals = await clips.GetCategoryTotalsByOwner(accountId);
        Dictionary<Guid, GameCategory> categories = (await gameCategories.GetByIdsAsync(
                totals.Select(total => total.GameCategoryId).ToList()))
            .ToDictionary(category => category.Id);

        List<ActivityGame> games = totals
            .Where(total => total.ClipCount > 0 && categories.ContainsKey(total.GameCategoryId))
            .Select(total =>
            {
                GameCategory category = categories[total.GameCategoryId];
                return new ActivityGame(category.Id, category.Name, category.ClothColor, (int)total.ClipCount);
            })
            .OrderByDescending(game => game.ClipCount)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ActivityGamesResponse(games);
    }
}
