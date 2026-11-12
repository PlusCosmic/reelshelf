using Reelshelf.StorageCheck;
using Xunit;

namespace Reelshelf.Test.StorageCheck;

public class CheckSettingsTests
{
    private static readonly Dictionary<string, string> AwsStore = new()
    {
        ["Storage__Region"] = "eu-north-1",
        ["Storage__Bucket"] = "media-bucket",
        ["Storage__AccessKeyId"] = "AKIAEXAMPLE",
        ["Storage__SecretAccessKey"] = "secret-value",
        ["Storage__MediaBaseUrl"] = "https://media.example"
    };

    private static CheckSettings Parse(Dictionary<string, string> environment, params string[] args) =>
        Parse(environment, _ => throw new FileNotFoundException(), args);

    private static CheckSettings Parse(Dictionary<string, string> environment,
        Func<string, IReadOnlyList<string>> readFile, params string[] args) =>
        CheckSettings.Parse(["--site-origin", "https://reelshelf.app", .. args], environment, readFile)
        ?? throw new InvalidOperationException("help");

    [Fact]
    public void R2SettingsInTheEnvironment_AreIgnored()
    {
        // What `infisical run --env=prod` injects while prod is still on R2: the API itself would refuse both sections.
        Dictionary<string, string> environment = new(AwsStore)
        {
            ["R2__AccountId"] = "0123456789abcdef",
            ["R2__AccessKeyId"] = "r2-key",
            ["R2__SecretAccessKey"] = "r2-secret",
            ["R2__Bucket"] = "r2-bucket",
            ["R2__MediaBaseUrl"] = "https://media.example",
            ["DatabaseConnectionString"] = "Host=db;Password=hunter2",
            ["Moderation__Cloudflare__ApiToken"] = "cloudflare-token"
        };

        CheckSettings settings = Parse(environment);

        Assert.Equal("media-bucket", settings.Store.Bucket);
        Assert.Equal("eu-north-1", settings.Store.Region);
        Assert.Equal("", settings.Store.AccountId);
        Assert.Null(settings.Store.ServiceUrl);
        Assert.Equal(
            ["R2__AccessKeyId", "R2__AccountId", "R2__Bucket", "R2__MediaBaseUrl", "R2__SecretAccessKey"],
            settings.Ignored);
        Assert.DoesNotContain(settings.Sources, source => source.Name.StartsWith("R2", StringComparison.Ordinal));
        Assert.All(settings.Sources, source => Assert.Equal("environment", source.Source));
    }

    [Fact]
    public void AnEnvFile_SuppliesTheKey_AndWinsOverTheEnvironment()
    {
        Dictionary<string, string> environment = new(AwsStore) { ["Storage__AccessKeyId"] = "from-environment" };
        string[] file =
        [
            "# written by secrets.sh",
            "",
            "Storage__AccessKeyId=AKIAFROMFILE",
            "export Storage__SecretAccessKey=a/b+c=d==",
            "Unrelated__Setting=whatever"
        ];

        CheckSettings settings = Parse(environment, path => path == "/keys.env" ? file : throw new FileNotFoundException(),
            "--env-file", "/keys.env");

        Assert.Equal("AKIAFROMFILE", settings.Store.AccessKeyId);
        // Everything after the first '=' is the value, taken literally.
        Assert.Equal("a/b+c=d==", settings.Store.SecretAccessKey);
        Assert.Contains(new SettingSource("Storage:AccessKeyId", "--env-file keys.env"), settings.Sources);
        Assert.Contains(new SettingSource("Storage:Bucket", "environment"), settings.Sources);
    }

    [Fact]
    public void AMalformedEnvFileLine_IsRefusedByNumber_WithoutItsValue()
    {
        UsageException error = Assert.Throws<UsageException>(() => Parse(new Dictionary<string, string>(AwsStore),
            _ => ["Storage__Bucket=media", "not a setting sk-very-secret"], "--env-file", "/keys.env"));

        Assert.Contains("line 2", error.Message);
        Assert.DoesNotContain("sk-very-secret", error.Message);
    }

    [Theory]
    [InlineData("--storage-secret-access-key", "x")]
    [InlineData("--storage-access-key-id", "x")]
    [InlineData("--Storage:SecretAccessKey=x")]
    public void TheKey_IsNeverTakenFromTheCommandLine(params string[] args)
    {
        UsageException error = Assert.Throws<UsageException>(() => Parse(new Dictionary<string, string>(AwsStore), args));

        Assert.Contains("never taken from the command line", error.Message);
    }

    [Fact]
    public void APartialStore_NamesWhatIsMissing_WithNoValues()
    {
        Dictionary<string, string> environment = new()
        {
            ["Storage__Region"] = "eu-north-1",
            ["Storage__SecretAccessKey"] = "secret-value"
        };

        UsageException error = Assert.Throws<UsageException>(() => Parse(environment));

        Assert.Contains("Storage:AccessKeyId", error.Message);
        Assert.Contains("Storage:Bucket", error.Message);
        Assert.DoesNotContain("secret-value", error.Message);
    }

    [Fact]
    public void NoStore_IsAUsageError()
    {
        Assert.Throws<UsageException>(() => Parse(new Dictionary<string, string> { ["R2__Bucket"] = "r2" }));
    }

    [Fact]
    public void TheSiteOrigin_IsRequired()
    {
        UsageException error = Assert.Throws<UsageException>(() =>
            CheckSettings.Parse([], new Dictionary<string, string>(AwsStore), _ => []));

        Assert.Contains("--site-origin", error.Message);
    }

    [Fact]
    public void ADistribution_NeedsTheStoreOnAws()
    {
        Dictionary<string, string> environment = new(AwsStore)
        {
            ["Storage__Endpoint"] = "https://127.0.0.1:9000",
            ["Moderation__CloudFront__DistributionId"] = "E123"
        };

        Assert.Throws<UsageException>(() => Parse(environment));
    }

    [Fact]
    public void TheToolsOwnOptions_AreRead()
    {
        Dictionary<string, string> environment = new(AwsStore)
        {
            ["Moderation__EvidenceBucket"] = "evidence-bucket",
            ["Moderation__CloudFront__DistributionId"] = "E123"
        };

        CheckSettings settings = Parse(environment, "--media-connect-host", "d111.cloudfront.net", "--no-purge-wait",
            "--purge-wait-seconds=60", "--other-origin", "https://other.example/");

        Assert.Equal("evidence-bucket", settings.EvidenceBucket);
        Assert.Equal("E123", settings.DistributionId);
        Assert.Equal("d111.cloudfront.net", settings.MediaConnectHost);
        Assert.Null(settings.MediaConnectPort);
        Assert.False(settings.PurgeWait);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.PurgeWaitLimit);
        Assert.Equal("https://other.example", settings.OtherOrigin);
        Assert.Equal("https://reelshelf.app", settings.SiteOrigin);
    }

    [Fact]
    public void AConnectHost_MayNameAPort()
    {
        CheckSettings settings = Parse(new Dictionary<string, string>(AwsStore), "--media-connect-host", "127.0.0.1:9443");

        Assert.Equal("127.0.0.1", settings.MediaConnectHost);
        Assert.Equal(9443, settings.MediaConnectPort);
    }

    [Fact]
    public void UnknownArguments_AreRefused()
    {
        Assert.Throws<UsageException>(() => Parse(new Dictionary<string, string>(AwsStore), "--bucket", "x"));
    }

    [Fact]
    public void Help_ReturnsNoSettings()
    {
        Assert.Null(CheckSettings.Parse(["--help"], new Dictionary<string, string>(), _ => []));
    }
}
