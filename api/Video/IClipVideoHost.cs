using Reelshelf.Games;

namespace Reelshelf.Video;

/// <summary>
/// The service that stores, encodes and serves clip videos. Everything outside the host's own folder goes through
/// this rather than a provider's client. Each clip records the host that holds its video (<c>clip.video_host</c>,
/// one of <see cref="VideoHostNames"/>) and <see cref="ClipVideoHosts"/> routes its calls there; hosts are
/// registered under their name with <see cref="VideoHostRegistration.AddClipVideoHost{THost, TMediaUrls}"/>.
/// <see cref="Bunny.BunnyService"/> (Bunny Stream) and <see cref="R2.R2VideoHost"/> (our own pipeline) implement it.
/// </summary>
public interface IClipVideoHost
{
    /// <summary>
    /// Creates an empty video for the owner's clip <paramref name="clipId"/> in <paramref name="gameCategory"/>, ready
    /// for its bytes to be uploaded. A host with containers files it in the owner's container for the category,
    /// creating that if need be; a host that keys its storage by clip may use the clip id as the video id.
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

    /// <summary>
    /// Whether the clip's status has to be fetched from the host (<see cref="Core.ClipStatusRefreshService"/>, the
    /// metadata backfill). False for a host whose status reaches the clip row through the app itself, as our own
    /// transcode worker's reports do: polling it would only read the row back.
    /// </summary>
    bool NeedsStatusPolling => true;

    /// <summary>Idempotent: a video the host no longer knows about counts as deleted.</summary>
    Task DeleteVideoAsync(Guid videoId);

    /// <summary>
    /// Idempotent like <see cref="DeleteVideoAsync"/>: a container the host no longer knows about counts as deleted.
    /// A host that doesn't file videos in containers has none to delete and does nothing.
    /// </summary>
    Task DeleteContainerAsync(Guid containerId);
}

/// <summary>A video just created at the host, with the container it was filed in.</summary>
public sealed record HostedVideo(Guid VideoId, Guid ContainerId, HostedVideoInfo Info);

/// <summary>What the host reports about a video, as stored on its <c>clip</c> row.</summary>
/// <param name="Status">The host's status mapped onto the app's own, stored in <c>clip.video_state</c>.</param>
/// <param name="ThumbnailFileName">The thumbnail's file name, or null when the host no longer stores one.</param>
/// <param name="HostStatus">
/// Bunny's own status number, written only to the legacy <c>clip.video_status</c> column so the previous app
/// image keeps working during a deploy. Nothing reads it; it goes when that column is dropped. Null for a host with
/// no such number, whose clips leave the column NULL.
/// </param>
public sealed record HostedVideoInfo(
    string Title,
    int LengthSeconds,
    ClipVideoStatus Status,
    int EncodeProgress,
    long StorageSize,
    DateTimeOffset UploadedAt,
    string? ThumbnailFileName,
    int? HostStatus);
