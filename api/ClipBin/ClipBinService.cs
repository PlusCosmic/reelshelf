using Microsoft.Extensions.Options;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Exceptions;

namespace Reelshelf.ClipBin;

/// <summary>
/// The bin: deleting a clip puts it here, out of everything but its owner's bin, until it is restored, deleted for
/// good, or the sweep deletes it after <see cref="ClipBinOptions.Retention"/>. A binned clip still counts toward the
/// owner's plan, so the bin never makes room for more clips than the plan keeps.
/// </summary>
public sealed class ClipBinService(
    ClipsStatements clips,
    ClipService clipService,
    IOptionsMonitor<ClipBinOptions> options,
    ILogger<ClipBinService> logger)
{
    /// <summary>The most ids one call takes, so a request stays one modest UPDATE.</summary>
    public const int MaxClipIds = 1000;

    /// <summary>
    /// The most clips one call deletes for good: each is deleted at its video host in turn, so a whole bin is emptied
    /// over several calls rather than one long request.
    /// </summary>
    public const int MaxDeletesPerCall = 25;

    public async Task<BinnedClipsResponse> Bin(Guid userId, BinClipsRequest request)
    {
        if ((request.ClipIds is null) == (request.Filter is null))
        {
            throw new BadRequestException("Give either clip_ids or filter");
        }

        if (request.ClipIds is { } ids)
        {
            CheckIds(ids);
            return new BinnedClipsResponse(await clips.BinClips(userId, ids));
        }

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
        List<Guid> binned = await clips.BinMatchingClips(userId, filter.CategoryId, tags, filter.Search, legend,
            filter.UnviewedOnly);
        logger.LogInformation("Account {UserId} binned {Count} clips matching a filter", userId, binned.Count);
        return new BinnedClipsResponse(binned);
    }

    public async Task<BinnedClipsResponse> Restore(Guid userId, RestoreClipsRequest request)
    {
        CheckIds(request.ClipIds);
        return new BinnedClipsResponse(await clips.RestoreClips(userId, request.ClipIds));
    }

    public async Task<ClipBinResponse> GetBin(Guid userId, int page, int pageSize)
    {
        PagedClipsResponse result = await clipService.GetClips(userId, null, page, pageSize, binned: true);
        return new ClipBinResponse(result.Clips, result.TotalClips, result.TotalPages,
            (int)Math.Ceiling(options.CurrentValue.Retention.TotalDays));
    }

    /// <summary>
    /// Deletes binned clips for good, through <see cref="ClipService.DeleteClip"/> as deleting one always did. A clip held
    /// for review can't be deleted yet and stays in the bin, as it would if its owner deleted it directly.
    /// </summary>
    public async Task<DeleteBinnedClipsResponse> DeleteForever(Guid userId, DeleteBinnedClipsRequest request)
    {
        if (request.ClipIds is { } requested)
        {
            CheckIds(requested);
        }

        List<Guid> due = await clips.GetBinnedClipIds(userId, request.ClipIds, MaxDeletesPerCall);
        List<Guid> deleted = [];
        List<Guid> held = [];
        foreach (Guid clipId in due)
        {
            try
            {
                if (await clipService.DeleteClip(clipId, userId))
                {
                    deleted.Add(clipId);
                }
            }
            catch (ConflictException)
            {
                held.Add(clipId);
            }
        }

        return new DeleteBinnedClipsResponse(deleted, held, await clips.CountBinnedClips(userId));
    }

    private static void CheckIds(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count is 0 or > MaxClipIds)
        {
            throw new BadRequestException($"Give between 1 and {MaxClipIds} clip ids");
        }
    }
}
