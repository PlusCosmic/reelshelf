using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipBin;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.Core;

/// <summary>
/// Moves clips to another book: these ids, or every clip matching <paramref name="Filter"/> (the library list's own
/// filters, so "select all that match" covers clips the page hasn't loaded). Exactly one of the two.
/// </summary>
/// <param name="CategoryId">The book the clips go to.</param>
public sealed record MoveClipsRequest(Guid CategoryId, List<Guid>? ClipIds, BinClipsFilter? Filter);

/// <summary>A clip that moved, and the book it came from, so the move can be undone.</summary>
public sealed record MovedClip(Guid ClipId, Guid FromCategoryId);

/// <param name="Moved">The clips that moved.</param>
/// <param name="SkippedIds">
/// Clips that stayed where they were because the book already holds the same file (an upload's duplicate check works
/// per book), or because another clip with the same file moved there in the same call.
/// </param>
public sealed record MoveClipsResponse(List<MovedClip> Moved, List<Guid> SkippedIds);

/// <summary>
/// Moves clips between an owner's books, for clips filed under the wrong game. Only the clip's book changes: its video,
/// tags, collections, share links and plan usage stay as they are, and a Bunny video stays in the collection it was
/// uploaded to (see V59). Clips moved into Apex Legends get the automatic legend detection they'd have had there.
/// </summary>
public sealed class ClipMoveService(
    ClipsStatements clips,
    GameCategoryStatements gameCategories,
    LegendDetectionService legendDetection,
    ILogger<ClipMoveService> logger)
{
    /// <summary>The most ids one call takes, as the bin does.</summary>
    public const int MaxClipIds = ClipBinService.MaxClipIds;

    public async Task<MoveClipsResponse> Move(Guid userId, MoveClipsRequest request)
    {
        if ((request.ClipIds is null) == (request.Filter is null))
        {
            throw new BadRequestException("Give either clip_ids or filter");
        }

        if (await gameCategories.GetByIdAsync(request.CategoryId) is null)
        {
            throw new NotFoundException("Game category", request.CategoryId);
        }

        List<ClipsStatements.MoveClipRow> rows;
        if (request.ClipIds is { } ids)
        {
            if (ids.Count is 0 or > MaxClipIds)
            {
                throw new BadRequestException($"Give between 1 and {MaxClipIds} clip ids");
            }

            rows = await clips.MoveClips(userId, request.CategoryId, ids);
        }
        else
        {
            BinClipsFilter filter = request.Filter!;
            string? legend = ApexLegendNames.Canonicalize(filter.Legend);
            if (!string.IsNullOrWhiteSpace(filter.Legend) && legend is null)
            {
                throw new BadRequestException("Unknown legend");
            }

            List<string>? tags = filter.Tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim().ToLowerInvariant())
                .ToList();
            rows = await clips.MoveClips(userId, request.CategoryId, null, filter.CategoryId, tags, filter.Search,
                legend, filter.UnviewedOnly);
        }

        List<MovedClip> moved = rows.Where(row => row.Moved)
            .Select(row => new MovedClip(row.ClipId, row.FromCategoryId))
            .ToList();
        List<Guid> skipped = rows.Where(row => !row.Moved).Select(row => row.ClipId).ToList();
        logger.LogInformation("Account {UserId} moved {Moved} clips to category {CategoryId} ({Skipped} skipped as duplicates)",
            userId, moved.Count, request.CategoryId, skipped.Count);

        // The move has happened; failing to queue detection only leaves the clips for the backfill.
        try
        {
            await legendDetection.QueueForMovedClipsAsync(moved.Select(clip => clip.ClipId).ToList(), request.CategoryId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to queue legend detection for clips moved to category {CategoryId}",
                request.CategoryId);
        }

        return new MoveClipsResponse(moved, skipped);
    }
}
