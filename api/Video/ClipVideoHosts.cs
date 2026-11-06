namespace Reelshelf.Video;

/// <summary>
/// Picks the video host for a clip by its <c>video_host</c>. Every per-clip host call (fetch, upload, delete) goes
/// through here rather than to a single host.
/// </summary>
public class ClipVideoHosts(IServiceProvider services)
{
    /// <summary>The host registered as <paramref name="videoHost"/>; throws when none is.</summary>
    public IClipVideoHost For(string videoHost)
    {
        return services.GetKeyedService<IClipVideoHost>(videoHost)
               ?? throw new InvalidOperationException($"No video host is registered as '{videoHost}'");
    }

    /// <summary>
    /// How <paramref name="videoHost"/>'s videos are downloaded straight from storage, or null when it offers no
    /// downloads.
    /// </summary>
    public IClipVideoDownloads? DownloadsFor(string videoHost) => services.GetKeyedService<IClipVideoDownloads>(videoHost);
}
