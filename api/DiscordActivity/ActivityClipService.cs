using Reelshelf.Bunny.Models;
using Reelshelf.Core;
using Reelshelf.Games;

namespace Reelshelf.DiscordActivity;

/// <summary>
/// One of a room member's own clips, as the Activity's picker shows it. The video id is the Bunny video guid;
/// the Activity streams it through its <c>/bunny-cdn</c> URL Mapping.
/// </summary>
public sealed record ActivityClip(
    Guid ClipId,
    Guid VideoId,
    string Title,
    string Game,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    bool Ready);

public sealed record ActivityClipsResponse(List<ActivityClip> Clips, bool HasMore);

/// <summary>Lists a room member's own clips for the Activity, newest first.</summary>
public sealed class ActivityClipService(ClipsStatements clips, GameCategoryStatements gameCategories)
{
    public const int PageSize = 24;

    public async Task<ActivityClipsResponse> GetOwnClips(Guid accountId, string? search, int page)
    {
        page = Math.Max(page, 1);
        ClipsStatements.PagedClipWithTagsRows rows = await clips.GetClipsWithTags(
            accountId,
            gameCategoryId: null,
            search: search,
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
                IsPlayable(row.VideoStatus)))
            .ToList();

        return new ActivityClipsResponse(items, page * PageSize < rows.TotalCount);
    }

    /// <summary>Bunny serves the HLS stream once the first resolution has finished encoding.</summary>
    public static bool IsPlayable(int? videoStatus) =>
        videoStatus is (int)BunnyVideoStatus.Finished or (int)BunnyVideoStatus.ResolutionFinished;
}
