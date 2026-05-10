using Reelshelf.Bunny.Models;
using Reelshelf.Core.Models;
using Reelshelf.Games;

namespace Reelshelf.Core;

public class ClipProjection(IConfiguration configuration)
{
    public Clip ProjectClip(
        ClipsStatements.ClipWithTagsRow row,
        GameCategory gameCategory,
        ClipsStatements.ClipCollectionRow collection,
        bool isViewed,
        bool isShared)
    {
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");
        int videoLibraryId = int.Parse(libraryId);

        BunnyVideo video = new(
            videoLibraryId,
            row.VideoId,
            row.Title ?? "Untitled",
            row.DateUploaded ?? DateTimeOffset.UtcNow,
            row.Length ?? 0,
            row.VideoStatus ?? 0,
            0,
            0,
            row.EncodeProgress ?? 0,
            row.StorageSize ?? 0,
            collection.CollectionId,
            row.ThumbnailFileName ?? string.Empty,
            string.Empty,
            gameCategory.Slug,
            [],
            []
        );

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
        ClipsStatements.ClipCollectionRow collection,
        ISet<Guid> viewedClipIds,
        ISet<Guid> sharedClipIds)
    {
        return rows
            .Select(row => ProjectClip(
                row,
                gameCategory,
                collection,
                viewedClipIds.Contains(row.Id),
                sharedClipIds.Contains(row.Id)))
            .ToList();
    }

    private static List<string> ParseTags(string? tagNames)
    {
        return tagNames?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];
    }
}
