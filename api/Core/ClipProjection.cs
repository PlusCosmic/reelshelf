using Reelshelf.Core.Models;
using Reelshelf.Games;

namespace Reelshelf.Core;

public class ClipProjection(ClipMediaUrls mediaUrls)
{
    public Clip ProjectClip(
        ClipsStatements.ClipWithTagsRow row,
        GameCategory gameCategory,
        bool isViewed,
        bool isShared)
    {
        ClipVideo video = new(
            row.VideoId,
            row.Title ?? "Untitled",
            row.Length ?? 0,
            row.VideoStatus,
            row.EncodeProgress ?? 0,
            row.StorageSize ?? 0,
            row.DateUploaded ?? row.CreatedAt,
            mediaUrls.Thumbnail(row.VideoId),
            mediaUrls.Sources(row.VideoId));

        return new Clip(
            row.Id,
            row.OwnerId,
            row.VideoId,
            row.GameCategoryId,
            gameCategory.Slug,
            row.CreatedAt,
            video,
            ParseTags(row.TagNames),
            isViewed,
            new ClipShareSummary(isShared),
            null);
    }

    public List<Clip> ProjectClips(
        IReadOnlyList<ClipsStatements.ClipWithTagsRow> rows,
        GameCategory gameCategory,
        ISet<Guid> viewedClipIds,
        ISet<Guid> sharedClipIds)
    {
        return rows
            .Select(row => ProjectClip(
                row,
                gameCategory,
                viewedClipIds.Contains(row.Id),
                sharedClipIds.Contains(row.Id)))
            .ToList();
    }

    /// <summary>
    /// Projects a page that may span categories, looking each clip's category up by id. A clip's category
    /// always exists (the foreign key cascades), so a row is only skipped if the category was deleted mid-request.
    /// </summary>
    public List<Clip> ProjectClips(
        IReadOnlyList<ClipsStatements.ClipWithTagsRow> rows,
        IReadOnlyDictionary<Guid, GameCategory> categoriesById,
        ISet<Guid> viewedClipIds,
        ISet<Guid> sharedClipIds)
    {
        List<Clip> clips = [];
        foreach (ClipsStatements.ClipWithTagsRow row in rows)
        {
            if (!categoriesById.TryGetValue(row.GameCategoryId, out GameCategory? category))
            {
                continue;
            }

            clips.Add(ProjectClip(
                row,
                category,
                viewedClipIds.Contains(row.Id),
                sharedClipIds.Contains(row.Id)));
        }

        return clips;
    }

    private static List<string> ParseTags(string? tagNames)
    {
        return tagNames?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];
    }
}
