using Reelshelf.Ops;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Ops;

public class OpsObjectStoreTests
{
    [Fact]
    public void AwsS3_IsNamedByItsRegionAndBucket_WithoutItsKeys()
    {
        OpsObjectStore store = OpsService.ObjectStore(new R2Options
        {
            Region = "eu-north-1",
            AccessKeyId = "AKIAEXAMPLE",
            SecretAccessKey = "very-secret",
            Bucket = "reelshelf-media",
            MediaBaseUrl = "https://media.reelshelf.test"
        });

        Assert.Equal(new OpsObjectStore(true, "s3", "eu-north-1", null, "reelshelf-media", "https://media.reelshelf.test"),
            store);
    }

    [Fact]
    public void AnS3CompatibleEndpoint_IsNamedByItsHost()
    {
        OpsObjectStore store = OpsService.ObjectStore(new R2Options
        {
            Endpoint = "https://objects.example.test",
            Region = "auto",
            AccessKeyId = "key",
            SecretAccessKey = "secret",
            Bucket = "clips",
            MediaBaseUrl = "https://media.example.test"
        });

        Assert.Equal("s3-compatible", store.Kind);
        Assert.Equal("objects.example.test", store.EndpointHost);
        Assert.Equal("auto", store.Region);
    }

    [Fact]
    public void NoStore_IsReportedAsUnconfigured()
    {
        Assert.Equal(new OpsObjectStore(false, null, null, null, null, null), OpsService.ObjectStore(null));
    }
}
