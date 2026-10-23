using Reelshelf.Billing;

namespace Reelshelf.Plans;

/// <summary>
/// What a tier may keep and add (ADR-0007). A null limit is no limit. Every tier's worst case at its limits stays well
/// inside its price: a clip is at most about 92 MB once encoded (120 s at 60 fps), stored in R2 for about $0.0009 a
/// month.
/// </summary>
public sealed record TierLimits(int? MaxClips, int? MaxClipSeconds, int? MaxNewClipsPer30Days)
{
    /// <summary>
    /// How far past the length cap a clip may run, since containers round: a replay buffer set to 60 s saves a
    /// clip a few hundredths of a second longer.
    /// </summary>
    public const double LengthToleranceSeconds = 1;

    public static readonly TimeSpan NewClipWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// New clips per 30 days is twice the clip limit, so filling a library and replacing some of it in the same month
    /// is fine; it only stops deleting and re-uploading for ever, since every upload costs an encode.
    /// </summary>
    public static TierLimits For(AccountTier tier) => tier switch
    {
        AccountTier.Free => new TierLimits(100, 60, 200),
        AccountTier.Plus => new TierLimits(1_000, 120, 2_000),
        AccountTier.Pro => new TierLimits(5_000, 120, 10_000),
        _ => new TierLimits(null, null, null)
    };

    /// <summary>Transcripts (and the summaries made from them) are for Pro and Unlimited.</summary>
    public static bool HasTranscripts(AccountTier tier) => tier >= AccountTier.Pro;

    /// <summary>Why a clip of this length can't be added, or null when it can (or the length isn't known).</summary>
    public string? LengthProblem(double? durationSeconds)
    {
        if (MaxClipSeconds is not { } max || durationSeconds is not { } duration ||
            duration <= max + LengthToleranceSeconds)
        {
            return null;
        }

        return $"This clip is {FormatSeconds(duration)} long, and your plan keeps clips of up to {FormatSeconds(max)}. " +
               "Trim it, or upgrade for longer clips.";
    }

    internal static string FormatSeconds(double seconds)
    {
        int whole = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return whole >= 60 && whole % 60 == 0 ? $"{whole / 60} min" : $"{whole} s";
    }
}
