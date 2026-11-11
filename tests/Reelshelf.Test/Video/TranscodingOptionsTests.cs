using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>The job types each pool of workers may claim (<c>Transcoding:WorkerJobTypes</c>), as configuration sets them.</summary>
public class TranscodingOptionsTests
{
    private const string AwsSecret = "aws-overflow-secret-0123456789abcdef-01234";

    [Fact]
    public void JobTypesFor_ReadsThePoolsList_IgnoringCaseAndSpaces()
    {
        TranscodingOptions options = Bind(("Transcoding:WorkerJobTypes:aws", " transcode, Trim ,thumbnails,,trim"));

        Assert.Equal(["transcode", "trim", "thumbnails"], options.JobTypesFor("aws"));
        Assert.Equal(["transcode", "trim", "thumbnails"], options.JobTypesFor("AWS"));
        Assert.Empty(options.WorkerJobTypeProblems());
    }

    [Fact]
    public void JobTypesFor_IsEveryType_ForAPoolWithNoEntryOrAnEmptyOne()
    {
        TranscodingOptions options = Bind(("Transcoding:WorkerJobTypes:gcp", " , "));

        Assert.Empty(options.JobTypesFor("default"));
        Assert.Empty(options.JobTypesFor("gcp"));
        Assert.Empty(options.JobTypesFor(null));
        Assert.Empty(new TranscodingOptions().JobTypesFor("aws"));
    }

    [Fact]
    public void AnUnknownJobType_FailsValidation_NamingThePoolAndTheType()
    {
        TranscodingOptions options = Bind(
            ("Transcoding:WorkerJobTypes:aws", "transcode,trims"),
            ("Transcoding:WorkerJobTypes:gcp", "transcode"));

        Assert.Equal(["Transcoding:WorkerJobTypes:aws has unknown job types: trims"], options.WorkerJobTypeProblems());
        ValidateOptionsResult result = new TranscodingOptionsValidation().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains("trims", result.FailureMessage);
        Assert.True(new TranscodingOptionsValidation().Validate(null, new TranscodingOptions()).Succeeded);
    }

    [Fact]
    public void PoolsWithoutSecret_AreTheOnesNoUsableWorkerSecretIsNamedAfter()
    {
        TranscodingOptions options = Bind(
            ("Transcoding:WorkerJobTypes:aws", "transcode"),
            ("Transcoding:WorkerJobTypes:asw", "transcode"),
            ("Transcoding:WorkerJobTypes:default", "transcode"));
        WorkerOptions workers = new() { Secrets = { ["AWS"] = AwsSecret } };

        Assert.Equal(["asw", "default"], WorkerSecretsCheck.PoolsWithoutSecret(workers, options));
        workers.Secret = AwsSecret;
        Assert.Equal(["asw"], WorkerSecretsCheck.PoolsWithoutSecret(workers, options));
    }

    private static TranscodingOptions Bind(params (string Key, string Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        return configuration.GetSection(TranscodingOptions.SectionName).Get<TranscodingOptions>() ?? new TranscodingOptions();
    }
}
