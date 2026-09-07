using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Exceptions;

namespace Reelshelf.Storage;

/// <summary>
/// Resolves a user's storage tier and enforces it before a clip is created.
/// Users listed in whitelist.json get unlimited storage; everyone else gets the configured default limit
/// (<c>Storage:DefaultLimitBytes</c>, 25 GiB when unset).
/// </summary>
public class StorageQuotaService(
    WhitelistService whitelistService,
    ClipsStatements clipsStatements,
    IConfiguration configuration)
{
    private readonly long _defaultLimitBytes =
        configuration.GetValue<long?>("Storage:DefaultLimitBytes") ?? StorageQuota.DefaultLimitBytes;

    public long? GetLimitBytes(string discordId)
    {
        return whitelistService.IsWhitelisted(discordId) ? null : _defaultLimitBytes;
    }

    public async Task<StorageQuota> GetQuota(Guid userId, string discordId)
    {
        long usedBytes = await clipsStatements.GetStorageUsedBytesByOwner(userId);
        return new StorageQuota(usedBytes, GetLimitBytes(discordId));
    }

    /// <summary>
    /// Throws <see cref="StorageQuotaExceededException"/> if storing <paramref name="fileSize"/> more bytes
    /// would exceed the user's limit.
    /// </summary>
    public async Task EnsureCanStore(Guid userId, string discordId, long fileSize)
    {
        StorageQuota quota = await GetQuota(userId, discordId);
        if (quota.CanStore(fileSize))
        {
            return;
        }

        throw new StorageQuotaExceededException(
            $"Storage limit reached. This clip is {FormatBytes(fileSize)} but only {FormatBytes(quota.RemainingBytes ?? 0)} " +
            $"of your {FormatBytes(quota.LimitBytes ?? 0)} remain. Delete some clips to free up space.");
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
