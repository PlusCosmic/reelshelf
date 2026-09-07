namespace Reelshelf.Storage;

/// <summary>
/// The caller's storage tier. <see cref="LimitBytes"/> is null on the unlimited tier.
/// </summary>
public sealed record StorageUsageResponse(long UsedBytes, long? LimitBytes, bool IsUnlimited);
