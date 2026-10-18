using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

public static class WorkerSetup
{
    /// <summary>The worker's services and its loop, configured from the <c>Worker</c> section.</summary>
    public static IServiceCollection AddTranscodeWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkerSettings>()
            .Bind(configuration.GetSection(WorkerSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerSettings>, WorkerSettingsValidation>();

        services.AddHttpClient(WorkerApiClient.HttpClientName, (provider, client) =>
        {
            WorkerSettings settings = provider.GetRequiredService<IOptions<WorkerSettings>>().Value;
            client.BaseAddress = new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Secret.Trim());
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        // Storage transfers carry no credential: the URLs are presigned. Files stream to and from disk; an hour
        // covers the largest upload.
        services.AddHttpClient(MediaTransfer.HttpClientName, client => client.Timeout = TimeSpan.FromHours(1));

        services.AddSingleton<WorkerApiClient>();
        services.AddSingleton<MediaTransfer>();
        services.AddSingleton<FfmpegRunner>();
        services.AddSingleton<HlsRemux>();
        services.AddSingleton<JobRunner>();
        services.AddSingleton<WorkerLoop>();
        services.AddHostedService(provider => provider.GetRequiredService<WorkerLoop>());
        return services;
    }
}

/// <summary>Names what is wrong with the settings, so the worker fails at startup with a useful message.</summary>
internal sealed class WorkerSettingsValidation : IValidateOptions<WorkerSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkerSettings options) =>
        options.Problem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
