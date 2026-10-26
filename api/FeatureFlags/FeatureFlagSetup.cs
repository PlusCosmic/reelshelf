using PostHog;
using PostHog.Config;

namespace Reelshelf.FeatureFlags;

public static class FeatureFlagSetup
{
    /// <summary>EU Cloud (ADR-0009); the SDK's own default is the US host.</summary>
    public static readonly Uri DefaultHostUrl = new("https://eu.i.posthog.com");

    /// <summary>
    /// Each download of the definitions is billed as 10 flag requests, so polling every 5 minutes (about 86,400 a month)
    /// keeps one instance well inside the free plan's million; the SDK's 30 seconds would use about 864,000.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the flag service. PostHog only where <c>PostHog:ProjectToken</c> is set, which is prod alone (one
    /// project on the free plan); anywhere else every flag is off unless <c>FeatureFlags:Overrides</c> turns it on.
    /// </summary>
    public static void AddFeatureFlags(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<FeatureFlagOptions>(builder.Configuration.GetSection(FeatureFlagOptions.SectionName));
        builder.Services.AddScoped<IFeatureFlagSubjects, FeatureFlagSubjects>();
        builder.Services.AddScoped<FeatureFlagService>();

        IConfigurationSection posthog = builder.Configuration.GetSection("PostHog");
        if (builder.Environment.IsEnvironment("OpenApi") || !UsesPostHog(posthog))
        {
            builder.Services.AddSingleton<IFeatureFlagEvaluator, NoFeatureFlagEvaluator>();
            return;
        }

        builder.Services.AddPostHog(options => options
            .UseConfigurationSection(posthog)
            .PostConfigure(settings =>
            {
                if (string.IsNullOrWhiteSpace(posthog["HostUrl"]))
                {
                    settings.HostUrl = DefaultHostUrl;
                }

                if (string.IsNullOrWhiteSpace(posthog["FeatureFlagPollInterval"]))
                {
                    settings.FeatureFlagPollInterval = DefaultPollInterval;
                }
            }));
        builder.Services.AddSingleton<IFeatureFlagEvaluator, PostHogFeatureFlagEvaluator>();
    }

    /// <summary>
    /// True when a project token is set. It needs a secret key too (a Personal API Key or a Project Secret API Key):
    /// without one the SDK can't evaluate locally, so it would ask PostHog on every check, sending the account's
    /// properties and spending the free plan's flag requests. That is refused at startup instead.
    /// </summary>
    internal static bool UsesPostHog(IConfigurationSection posthog)
    {
        if (string.IsNullOrWhiteSpace(posthog["ProjectToken"]))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(posthog["SecretKey"]))
        {
            throw new InvalidOperationException(
                "PostHog:ProjectToken is set without PostHog:SecretKey, which local flag evaluation needs.");
        }

        return true;
    }
}
