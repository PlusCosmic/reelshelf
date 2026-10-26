using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
using Xunit;

namespace Reelshelf.Test.FeatureFlags;

public class FeatureFlagSetupTests
{
    [Fact]
    public void WithoutAProjectTokenPostHogIsntUsed()
    {
        Assert.False(FeatureFlagSetup.UsesPostHog(Section([])));
    }

    [Fact]
    public void AProjectTokenWithASecretKeyUsesPostHog()
    {
        Assert.True(FeatureFlagSetup.UsesPostHog(Section(new()
        {
            ["PostHog:ProjectToken"] = "phc_x",
            ["PostHog:SecretKey"] = "phx_y"
        })));
    }

    [Fact]
    public void AProjectTokenWithoutASecretKeyFailsStartup()
    {
        // Without a secret key the SDK would ask PostHog on every check instead of evaluating locally.
        Assert.Throws<InvalidOperationException>(() =>
            FeatureFlagSetup.UsesPostHog(Section(new() { ["PostHog:ProjectToken"] = "phc_x" })));
    }

    [Fact]
    public void PostHogDefaultsToEuCloudAndAFiveMinutePoll()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostHog:ProjectToken"] = "phc_x",
            ["PostHog:SecretKey"] = "phx_y"
        });
        builder.AddFeatureFlags();

        using ServiceProvider services = builder.Services.BuildServiceProvider();
        PostHog.PostHogOptions options = services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<PostHog.PostHogOptions>>().Value;

        Assert.IsType<PostHogFeatureFlagEvaluator>(services.GetRequiredService<IFeatureFlagEvaluator>());
        Assert.Equal(FeatureFlagSetup.DefaultHostUrl, options.HostUrl);
        Assert.Equal(FeatureFlagSetup.DefaultPollInterval, options.FeatureFlagPollInterval);
    }

    [Fact]
    public void WithoutPostHogEveryFlagIsLeftToOverrides()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.AddFeatureFlags();

        using ServiceProvider services = builder.Services.BuildServiceProvider();

        Assert.IsType<NoFeatureFlagEvaluator>(services.GetRequiredService<IFeatureFlagEvaluator>());
    }

    [Fact]
    public void RequireFeatureRefusesAnUnknownKey()
    {
        Assert.Throws<ArgumentException>(() => new FakeEndpointBuilder().RequireFeature("no-such-flag"));
    }

    [Fact]
    public async Task RequireFeatureAnswers404WhileTheFlagIsOff()
    {
        object? result = await Invoke(new FeatureFlagServiceTests.FakeEvaluator(), "beta");

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task RequireFeatureRunsTheEndpointWhileTheFlagIsOn()
    {
        object? result = await Invoke(new FeatureFlagServiceTests.FakeEvaluator { Enabled = ["beta"] }, "beta");

        Assert.Equal("endpoint ran", result);
    }

    private static async Task<object?> Invoke(IFeatureFlagEvaluator evaluator, string key)
    {
        ServiceCollection services = new();
        services.AddScoped(_ => FeatureFlagServiceTests.Service(evaluator));
        DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };
        http.Items[AuthenticatedUser.HttpContextKey] = new AuthenticatedUser(Guid.NewGuid(), "ada", null, null,
            null, true, UserRole.Editor, null, []);

        RequireFeatureFilter filter = new(key);
        return await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http),
            _ => ValueTask.FromResult<object?>("endpoint ran"));
    }

    private static IConfigurationSection Section(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("PostHog");
    }

    private sealed class FakeEndpointBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention)
        {
        }
    }
}
