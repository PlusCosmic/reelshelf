namespace Reelshelf.Storage;

/// <summary>
/// A user's storage tier: how much clip storage they have used and how much they are allowed.
/// <see cref="LimitBytes"/> is null for the unlimited tier.
/// </summary>
public sealed record StorageQuota(long UsedBytes, long? LimitBytes)
{
    /// <summary>Default free tier: 25 GiB.</summary>
    public const long DefaultLimitBytes = 25L * 1024 * 1024 * 1024;

    public bool IsUnlimited => LimitBytes is null;

    /// <summary>Bytes still available, or null when unlimited.</summary>
    public long? RemainingBytes => LimitBytes is { } limit ? Math.Max(0, limit - UsedBytes) : null;

    /// <summary>
    /// Largest file size a client may declare. Bounds bogus reservations so per-owner sums stay well inside int64.
    /// </summary>
    public const long MaxDeclaredFileSizeBytes = 1024L * 1024 * 1024 * 1024;

    /// <summary>True if a clip of <paramref name="fileSize"/> bytes fits within the remaining storage.</summary>
    public bool CanStore(long fileSize)
    {
        if (fileSize < 0 || fileSize > MaxDeclaredFileSizeBytes)
        {
            return false;
        }

        // Compare against the remaining space rather than adding, so a huge fileSize cannot wrap negative.
        return LimitBytes is not { } limit || fileSize <= limit - UsedBytes;
    }
}
