using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.R2;

public static class R2Setup
{
    /// <summary>
    /// Registers the <c>r2</c> video host and R2 as the transcode jobs' storage when <c>R2:*</c> is configured;
    /// otherwise registers neither, so <c>VideoHost:NewUploads=r2</c> fails at startup
    /// (<see cref="VideoHostRegistration.AddClipVideoHostRouting"/>) and transcode jobs get no URLs. Call it before
    /// the routing.
    /// </summary>
    public static IServiceCollection AddR2VideoHost(this IServiceCollection services, IConfiguration configuration)
    {
        R2Options? options = R2Options.FromConfiguration(configuration);
        if (options is null)
        {
            services.AddSingleton<ITranscodeStorage, UnconfiguredTranscodeStorage>();
            return services;
        }

        services.AddSingleton(options);
        services.AddSingleton<R2Storage>();
        services.AddSingleton<ITranscodeStorage>(provider => provider.GetRequiredService<R2Storage>());
        services.AddClipVideoHost<R2VideoHost, R2MediaUrls>(VideoHostNames.R2);
        services.AddKeyedSingleton<IClipVideoDownloads, R2Downloads>(VideoHostNames.R2);
        return services;
    }
}
