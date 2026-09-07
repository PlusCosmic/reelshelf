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

    /// <summary>True if a clip of <paramref name="fileSize"/> bytes fits within the remaining storage.</summary>
    public bool CanStore(long fileSize)
    {
        return LimitBytes is not { } limit || UsedBytes + fileSize <= limit;
    }
}
