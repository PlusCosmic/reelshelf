using PostHog;
using PostHog.Config;

namespace Reelshelf.FeatureFlags;

/// <summary>
/// PostHog's settings (<c>PostHog:*</c>), set on prod only: the free plan has one project. Null without a project
/// token. A token without the secret key fails startup, since the SDK would otherwise ask PostHog on every check.
/// Without the project id flags still work, but no people are sent (<see cref="SyncsPeople"/>), since they couldn't be
/// deleted with their accounts.
/// </summary>
public sealed record PostHogSettings(
    string ProjectToken,
    string SecretKey,
    string? ProjectId,
    Uri HostUrl,
    Uri ApiHostUrl,
    TimeSpan PersonSyncInterval)
{
    /// <summary>EU Cloud's ingestion host (ADR-0009); the SDK's own default is the US host.</summary>
    public static readonly Uri DefaultHostUrl = new("https://eu.i.posthog.com");

    /// <summary>EU Cloud's app and REST API host, where people are deleted.</summary>
    public static readonly Uri DefaultApiHostUrl = new("https://eu.posthog.com");

    /// <summary>How often the person sync looks for accounts whose properties changed; a change of access wakes it sooner.</summary>
    public static readonly TimeSpan DefaultPersonSyncInterval = TimeSpan.FromMinutes(15);

    public static PostHogSettings? FromConfiguration(IConfigurationSection section)
    {
        string? token = section["ProjectToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string secretKey = Require(section, "SecretKey",
            "which local flag evaluation and deleting people need (a Personal API Key with feature_flag:read and person:write)");
        string? projectId = string.IsNullOrWhiteSpace(section["ProjectId"]) ? null : section["ProjectId"]!.Trim();

        return new PostHogSettings(
            token.Trim(),
            secretKey,
            projectId,
            Url(section, "HostUrl") ?? DefaultHostUrl,
            Url(section, "ApiHostUrl") ?? DefaultApiHostUrl,
            section.GetValue<TimeSpan?>("PersonSyncInterval") ?? DefaultPersonSyncInterval);
    }

    /// <summary>Whether accounts are identified to PostHog: only when their people can be deleted too.</summary>
    public bool SyncsPeople => ProjectId is not null;

    private static string Require(IConfigurationSection section, string key, string why)
    {
        string? value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"PostHog:ProjectToken is set without PostHog:{key}, {why}.");
        }

        return value.Trim();
    }

    private static Uri? Url(IConfigurationSection section, string key)
    {
        string? value = section[key];
        return string.IsNullOrWhiteSpace(value) ? null : new Uri(value);
    }
}

public static class FeatureFlagSetup
{
    /// <summary>
    /// Each download of the definitions is billed as 10 flag requests, so polling every 5 minutes (about 86,400 a month)
    /// keeps one instance well inside the free plan's million; the SDK's 30 seconds would use about 864,000.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the flag service. PostHog only where <c>PostHog:ProjectToken</c> is set, which is prod alone (one
    /// project on the free plan); anywhere else every flag is off unless <c>FeatureFlags:Overrides</c> turns it on, and
    /// nothing is sent to PostHog.
    /// </summary>
    public static void AddFeatureFlags(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<FeatureFlagOptions>(builder.Configuration.GetSection(FeatureFlagOptions.SectionName));
        builder.Services.AddScoped<IFeatureFlagSubjects, FeatureFlagSubjects>();
        builder.Services.AddScoped<FeatureFlagService>();
        builder.Services.AddSingleton<PostHogPersonSyncSignal>();

        IConfigurationSection posthog = builder.Configuration.GetSection("PostHog");
        PostHogSettings? settings = builder.Environment.IsEnvironment("OpenApi")
            ? null
            : PostHogSettings.FromConfiguration(posthog);
        if (settings is null)
        {
            builder.Services.AddSingleton<IFeatureFlagEvaluator, NoFeatureFlagEvaluator>();
            builder.Services.AddSingleton<IPostHogPeople, NoPostHogPeople>();
            return;
        }

        builder.Services.AddSingleton(settings);
        builder.Services.AddPostHog(options => options
            .UseConfigurationSection(posthog)
            .PostConfigure(sdk =>
            {
                sdk.HostUrl = settings.HostUrl;
                if (string.IsNullOrWhiteSpace(posthog["FeatureFlagPollInterval"]))
                {
                    sdk.FeatureFlagPollInterval = DefaultPollInterval;
                }
            }));
        builder.Services.AddSingleton<IFeatureFlagEvaluator, PostHogFeatureFlagEvaluator>();

        if (!settings.SyncsPeople)
        {
            builder.Services.AddSingleton<IPostHogPeople, NoPostHogPeople>();
            builder.Services.AddHostedService<PostHogPersonSyncOffWarning>();
            return;
        }

        builder.Services.AddHttpClient<IPostHogPeople, PostHogPeopleClient>(PostHogPeopleClient.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddScoped<IPostHogPersonStore, PostHogPersonStatements>();
        builder.Services.AddScoped<PostHogPersonSync>();
        builder.Services.AddHostedService<PostHogPersonSyncService>();
    }
}

/// <summary>Says at startup that PostHog is configured for flags but sends no people, and what turns that on.</summary>
public sealed class PostHogPersonSyncOffWarning(ILogger<PostHogPersonSyncOffWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "PostHog:ProjectId isn't set, so accounts aren't identified to PostHog and its UI won't list the role, tier " +
            "and granted_tier properties; flags still work");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
