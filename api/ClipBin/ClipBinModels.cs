using Reelshelf.Core.Models;

namespace Reelshelf.ClipBin;

/// <summary>
/// Which clips go in the bin: these ids, or every clip matching <paramref name="Filter"/> (the library's own filters, so
/// "select all that match" covers clips the page hasn't loaded). Exactly one of the two.
/// </summary>
public sealed record BinClipsRequest(List<Guid>? ClipIds, BinClipsFilter? Filter);

/// <summary>The library list's filters, as <c>GET /api/clips</c> takes them; a null category spans every game.</summary>
public sealed record BinClipsFilter(
    Guid? CategoryId,
    List<string>? Tags,
    string? Search,
    string? Legend,
    bool UnviewedOnly = false);

/// <summary>The clips the call moved, so the caller can offer to undo exactly those.</summary>
public sealed record BinnedClipsResponse(List<Guid> ClipIds);

public sealed record RestoreClipsRequest(List<Guid> ClipIds);

/// <summary>Deletes these binned clips for good, or, with no ids, the next part of the whole bin.</summary>
public sealed record DeleteBinnedClipsRequest(List<Guid>? ClipIds);

/// <param name="DeletedIds">The clips deleted for good.</param>
/// <param name="HeldIds">Clips that can't be deleted right now; they stay in the bin.</param>
/// <param name="Remaining">How many clips are still in the bin, held ones included.</param>
public sealed record DeleteBinnedClipsResponse(List<Guid> DeletedIds, List<Guid> HeldIds, int Remaining);

/// <summary>A page of the bin, newest in first.</summary>
/// <param name="RetentionDays">How many days a clip stays in the bin before it is deleted for good.</param>
public sealed record ClipBinResponse(List<Clip> Clips, long TotalClips, long TotalPages, int RetentionDays);
