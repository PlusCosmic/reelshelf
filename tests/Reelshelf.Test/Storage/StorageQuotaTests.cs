using Reelshelf.Storage;
using Xunit;

namespace Reelshelf.Test.Storage;

public class StorageQuotaTests
{
    private const long OneGib = 1024L * 1024 * 1024;

    [Fact]
    public void DefaultLimit_Is25GiB()
    {
        Assert.Equal(25 * OneGib, StorageQuota.DefaultLimitBytes);
    }

    [Fact]
    public void UnlimitedTier_HasNoLimitAndAlwaysStores()
    {
        StorageQuota quota = new(UsedBytes: 900 * OneGib, LimitBytes: null);

        Assert.True(quota.IsUnlimited);
        Assert.Null(quota.RemainingBytes);
        Assert.True(quota.CanStore(500 * OneGib));
        Assert.True(quota.CanStore(StorageQuota.MaxDeclaredFileSizeBytes));
    }

    [Theory]
    [InlineData(0, 25, true)]
    [InlineData(24, 1, true)]
    [InlineData(24, 2, false)]
    [InlineData(25, 1, false)]
    public void DefaultTier_CanStore_RespectsLimit(long usedGib, long fileGib, bool expected)
    {
        StorageQuota quota = new(usedGib * OneGib, StorageQuota.DefaultLimitBytes);

        Assert.False(quota.IsUnlimited);
        Assert.Equal(expected, quota.CanStore(fileGib * OneGib));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(StorageQuota.MaxDeclaredFileSizeBytes + 1)]
    [InlineData(-1)]
    public void CanStore_RejectsSizesThatCouldOverflow(long fileSize)
    {
        StorageQuota limited = new(UsedBytes: OneGib, LimitBytes: StorageQuota.DefaultLimitBytes);
        StorageQuota unlimited = new(UsedBytes: OneGib, LimitBytes: null);

        Assert.False(limited.CanStore(fileSize));
        Assert.False(unlimited.CanStore(fileSize));
    }

    [Fact]
    public void RemainingBytes_NeverGoesNegative()
    {
        StorageQuota quota = new(UsedBytes: 30 * OneGib, LimitBytes: StorageQuota.DefaultLimitBytes);

        Assert.Equal(0, quota.RemainingBytes);
        Assert.False(quota.CanStore(1));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(25L * 1024 * 1024 * 1024, "25 GB")]
    [InlineData(1_200_000_000, "1.1 GB")]
    public void FormatBytes_UsesBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, StorageQuotaService.FormatBytes(bytes));
    }
}
