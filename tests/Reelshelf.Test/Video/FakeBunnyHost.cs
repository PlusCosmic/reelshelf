using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Games;
using Reelshelf.Video;

namespace Reelshelf.Test.Video;

/// <summary>
/// A video host that records the deletes it's asked for, standing in for Bunny (or any host) in deletion tests.
/// Videos and collections in <see cref="FailingVideos"/> and <see cref="FailingCollections"/> throw, as a 5xx would.
/// </summary>
internal sealed class FakeBunnyHost : IClipVideoHost
{
    private readonly Lock _lock = new();
    public List<Guid> DeletedVideos { get; } = [];
    public List<Guid> DeletedCollections { get; } = [];
    public HashSet<Guid> FailingVideos { get; } = [];
    public HashSet<Guid> FailingCollections { get; } = [];

    /// <summary>The host as <see cref="ClipVideoHosts"/> resolves it for Bunny, with <paramref name="r2"/> as R2.</summary>
    public ClipVideoHosts Hosts(IClipVideoHost? r2 = null)
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, this);
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, r2 ?? new FakeBunnyHost());
        return new ClipVideoHosts(services.BuildServiceProvider());
    }

    public Task DeleteVideoAsync(Guid videoId)
    {
        if (FailingVideos.Contains(videoId))
        {
            throw new HttpRequestException("Bunny answered 500");
        }

        lock (_lock)
        {
            DeletedVideos.Add(videoId);
        }

        return Task.CompletedTask;
    }

    public Task DeleteContainerAsync(Guid containerId)
    {
        if (FailingCollections.Contains(containerId))
        {
            throw new HttpRequestException("Bunny answered 500");
        }

        lock (_lock)
        {
            DeletedCollections.Add(containerId);
        }

        return Task.CompletedTask;
    }

    public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title) =>
        throw new NotSupportedException();

    public Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize) =>
        throw new NotSupportedException();

    public Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<HostedVideoInfo?> GetVideoAsync(Guid videoId) => throw new NotSupportedException();
}
