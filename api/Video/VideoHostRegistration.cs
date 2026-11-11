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
    /// Registers the routing for every host added with <see cref="AddClipVideoHost{THost, TMediaUrls}"/>. Call it
    /// after the hosts are added: with <paramref name="requireR2"/> it throws at startup when the <c>r2</c> host,
    /// which every new clip goes to, isn't registered (neither <c>Storage:*</c> nor <c>R2:*</c> set), rather than
    /// failing every upload.
    /// </summary>
    public static IServiceCollection AddClipVideoHostRouting(this IServiceCollection services, bool requireR2)
    {
        if (requireR2 && !IsRegistered(services, VideoHostNames.R2))
        {
            throw new InvalidOperationException(
                "No video host is registered: set Storage:Region (or Storage:Endpoint), Storage:AccessKeyId, Storage:SecretAccessKey, Storage:Bucket and Storage:MediaBaseUrl, or the older R2:AccountId, R2:AccessKeyId, R2:SecretAccessKey, R2:Bucket and R2:MediaBaseUrl");
        }

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
