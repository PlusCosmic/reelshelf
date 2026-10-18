namespace Reelshelf.Video;

public static class VideoHostRegistration
{
    /// <summary>
    /// Registers a video host and its media URLs under <paramref name="name"/>, one of <see cref="VideoHostNames"/>.
    /// This is all adding a host takes: clips whose <c>video_host</c> is that name are then routed to it.
    /// </summary>
    public static IServiceCollection AddClipVideoHost<THost, TMediaUrls>(this IServiceCollection services, string name)
        where THost : class, IClipVideoHost
        where TMediaUrls : class, IClipVideoMediaUrls
    {
        if (!VideoHostNames.All.Contains(name))
        {
            throw new ArgumentException($"'{name}' is not one of the video host names clip.video_host allows", nameof(name));
        }

        services.AddScoped<THost>();
        services.AddKeyedScoped<IClipVideoHost>(name, (provider, _) => provider.GetRequiredService<THost>());
        services.AddKeyedSingleton<IClipVideoMediaUrls, TMediaUrls>(name);
        return services;
    }

    /// <summary>
    /// Registers the routing for every host added with <see cref="AddClipVideoHost{THost, TMediaUrls}"/>, and the
    /// policy for new uploads from <c>VideoHost</c> configuration. Call it after the hosts are added: it throws at
    /// startup when <c>VideoHost:NewUploads</c> names a host that isn't registered, rather than failing every new
    /// upload, or quietly sending them to Bunny and leaving clips to move later.
    /// </summary>
    public static IServiceCollection AddClipVideoHostRouting(this IServiceCollection services, IConfiguration configuration)
    {
        NewUploadVideoHostPolicy policy = NewUploadVideoHostPolicy.FromConfiguration(configuration);
        if (!IsRegistered(services, policy.NewUploads))
        {
            throw new InvalidOperationException(
                $"VideoHost:NewUploads is '{policy.NewUploads}', but no video host is registered under that name");
        }

        services.AddSingleton(policy);
        services.AddScoped<ClipVideoHosts>();
        return services;
    }

    public static bool IsRegistered(IServiceCollection services, string name)
    {
        return services.Any(descriptor => descriptor.IsKeyedService
                                          && Equals(descriptor.ServiceKey, name)
                                          && descriptor.ServiceType == typeof(IClipVideoHost))
               && services.Any(descriptor => descriptor.IsKeyedService
                                             && Equals(descriptor.ServiceKey, name)
                                             && descriptor.ServiceType == typeof(IClipVideoMediaUrls));
    }
}
