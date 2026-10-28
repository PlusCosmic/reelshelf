using Reelshelf.Billing;

namespace Reelshelf.Plans;

/// <summary>An account's tier, its limits and how much of them it uses.</summary>
public sealed record AccountPlan(
    AccountTier Tier,
    TierLimits Limits,
    int ClipCount,
    int NewClipsLast30Days,
    long StorageUsedBytes)
{
    /// <summary>
    /// True when a plan that can be bought keeps more clips than this one, so offering an upgrade for room would help.
    /// </summary>
    public bool CanBuyMoreClips =>
        Limits.MaxClips is { } maxClips && TierLimits.Exceeds(TierLimits.BestToBuy.MaxClips, maxClips);

    /// <summary>
    /// Why a new clip can't be added, or null when it can. Clips over the limit (after a downgrade) are kept, but
    /// nothing is added until the account is back under it.
    /// </summary>
    public string? NewClipProblem()
    {
        if (Limits.MaxClips is { } maxClips && ClipCount >= maxClips)
        {
            return $"Your plan keeps up to {maxClips:N0} clips and you have {ClipCount:N0}. " +
                   (CanBuyMoreClips
                       ? "Delete some, or upgrade to keep more."
                       : "Delete some to add more.");
        }

        if (Limits.MaxNewClipsPer30Days is { } maxNew && NewClipsLast30Days >= maxNew)
        {
            return $"You've added {NewClipsLast30Days:N0} clips in the last 30 days, the most your plan allows. " +
                   (TierLimits.Exceeds(TierLimits.BestToBuy.MaxNewClipsPer30Days, maxNew)
                       ? "Try again later, or upgrade."
                       : "Try again later.");
        }

        return null;
    }
}
