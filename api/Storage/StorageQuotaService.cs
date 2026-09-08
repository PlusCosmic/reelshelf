using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Exceptions;
using Reelshelf.Users;

namespace Reelshelf.Storage;

/// <summary>
/// Resolves a user's storage tier and enforces it before a clip is created.
/// Users with a linked identity listed in whitelist.json get unlimited storage; everyone else gets the
/// configured default limit (<c>Storage:DefaultLimitBytes</c>, 25 GiB when unset).
/// </summary>
public class StorageQuotaService(
    WhitelistService whitelistService,
    ClipsStatements clipsStatements,
    UserStatements userStatements,
    IConfiguration configuration)
{
    private readonly long _defaultLimitBytes =
        configuration.GetValue<long?>("Storage:DefaultLimitBytes") ?? StorageQuota.DefaultLimitBytes;

    public long? GetLimitBytes(IEnumerable<UserIdentityRef> identities)
    {
        return whitelistService.IsWhitelisted(identities) ? null : _defaultLimitBytes;
    }

    public async Task<StorageQuota> GetQuota(AuthenticatedUser user)
    {
        long usedBytes = await clipsStatements.GetStorageUsedBytesByOwner(user.Id);
        return new StorageQuota(usedBytes, GetLimitBytes(user.Identities));
    }

    public async Task<StorageQuota> GetQuota(Guid userId)
    {
        List<UserStatements.UserIdentityRow> identities = await userStatements.GetIdentitiesForUser(userId);
        long usedBytes = await clipsStatements.GetStorageUsedBytesByOwner(userId);
        return new StorageQuota(usedBytes, GetLimitBytes(identities.Select(identity => identity.ToRef())));
    }

    /// <summary>
    /// Throws <see cref="StorageQuotaExceededException"/> if storing <paramref name="fileSize"/> more bytes
    /// would exceed the user's limit.
    /// </summary>
    public async Task EnsureCanStore(Guid userId, long fileSize)
    {
        StorageQuota quota = await GetQuota(userId);
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
