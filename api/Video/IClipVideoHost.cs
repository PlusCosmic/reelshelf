using Reelshelf.Games;

namespace Reelshelf.Video;

/// <summary>
/// The service that stores, encodes and serves clip videos. Everything outside the host's own folder goes through
/// this rather than a provider's client. Each clip records the host that holds its video (<c>clip.video_host</c>,
/// one of <see cref="VideoHostNames"/>) and <see cref="ClipVideoHosts"/> routes its calls there; hosts are
/// registered under their name with <see cref="VideoHostRegistration.AddClipVideoHost{THost, TMediaUrls}"/>.
/// <see cref="R2.R2VideoHost"/> (our own pipeline) is the only one.
/// </summary>
public interface IClipVideoHost
{
    /// <summary>
    /// Creates an empty video for the owner's clip <paramref name="clipId"/> in <paramref name="gameCategory"/>, ready
    /// for its bytes to be uploaded. A host that keys its storage by clip may use the clip id as the video id.
    /// </summary>
    Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title);

    /// <summary>
    /// Where the browser uploads the bytes of a video <see cref="CreateVideoAsync"/> made, authorised for that video
    /// only and for a limited time. <paramref name="title"/> is the title the video was created with, and
    /// <paramref name="fileSize"/> the size the clip reserved. A host may start the upload here, as R2 starts its
    /// multipart upload (<see cref="IMultipartUploadHost"/>).
    /// </summary>
    Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize);

    /// <summary>
    /// Streams a video file into a video <see cref="CreateVideoAsync"/> made. This is the server-side counterpart of
    /// the browser's upload, used when the bytes come from another service (Twitch) rather than the user's disk.
    /// </summary>
    Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken);

    /// <summary>The video as the host sees it now, or null when the host has no such video.</summary>
    Task<HostedVideoInfo?> GetVideoAsync(Guid videoId);

    /// <summary>Idempotent: a video the host no longer knows about counts as deleted.</summary>
    Task DeleteVideoAsync(Guid videoId);
}

/// <summary>A video just created at the host.</summary>
public sealed record HostedVideo(Guid VideoId, HostedVideoInfo Info);

/// <summary>What the host reports about a video, as stored on its <c>clip</c> row.</summary>
/// <param name="Status">The host's status mapped onto the app's own, stored in <c>clip.video_state</c>.</param>
/// <param name="ThumbnailFileName">The thumbnail's file name, or null when the host no longer stores one.</param>
public sealed record HostedVideoInfo(
    string Title,
    int LengthSeconds,
    ClipVideoStatus Status,
    int EncodeProgress,
    long StorageSize,
    DateTimeOffset UploadedAt,
    string? ThumbnailFileName);
