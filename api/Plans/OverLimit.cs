namespace Reelshelf.Plans;

/// <summary>A clip as going over the limit sees it: the owner's picks first, then the newest.</summary>
public sealed record OwnedClip(Guid Id, string? Title, DateTimeOffset CreatedAt, DateTimeOffset? KeptAt, string? GameName);

/// <summary>Which of an account's clips its tier keeps, and which are over the limit (ADR-0007).</summary>
public static class OverLimit
{
    /// <summary>
    /// The clips kept and the clips over <paramref name="limit"/>. Clips the owner picked come first (the most recently
    /// picked first, should there be more picks than room), then the rest newest first. No limit keeps everything.
    /// </summary>
    public static (List<OwnedClip> Kept, List<OwnedClip> Over) Split(IEnumerable<OwnedClip> clips, int? limit)
    {
        List<OwnedClip> ordered = clips
            .OrderByDescending(clip => clip.KeptAt is not null)
            .ThenByDescending(clip => clip.KeptAt)
            .ThenByDescending(clip => clip.CreatedAt)
            .ThenBy(clip => clip.Id)
            .ToList();
        if (limit is not { } max || ordered.Count <= max)
        {
            return (ordered, []);
        }

        return (ordered.Take(max).ToList(), ordered.Skip(max).ToList());
    }
}
