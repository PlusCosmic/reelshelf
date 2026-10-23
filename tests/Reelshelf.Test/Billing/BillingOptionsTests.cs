using Microsoft.Extensions.Configuration;
using Reelshelf.Billing;
using Xunit;

namespace Reelshelf.Test.Billing;

public class BillingOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();

    [Fact]
    public void WithoutASecretKey_BillingIsOff()
    {
        Assert.Null(BillingOptions.FromConfiguration(Configuration(("Billing:WebhookSecret", "whsec_1"))));
    }

    [Fact]
    public void ASecretKeyWithoutAWebhookSecret_FailsStartup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BillingOptions.FromConfiguration(Configuration(("Billing:SecretKey", "rk_test_1"))));
    }

    [Fact]
    public void ReturnPagesAreOnTheFrontendOrigin()
    {
        BillingOptions? options = BillingOptions.FromConfiguration(Configuration(
            ("Billing:SecretKey", "rk_test_1"),
            ("Billing:WebhookSecret", "whsec_1"),
            ("FrontendOrigin", "https://reelshelf.app/")));

        Assert.Equal("https://reelshelf.app", options?.FrontendOrigin);
    }
}
