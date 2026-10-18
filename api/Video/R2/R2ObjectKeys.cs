using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.R2;

/// <summary>
/// Where an R2 clip's objects live in the bucket. An R2 clip's video id is its clip id.
/// <list type="bullet">
/// <item><c>uploads/{clipId}</c>: the source as uploaded, deleted once its transcode succeeds.</item>
/// <item><c>clips/{clipId}/v{n}/</c>: version <c>n</c>'s outputs (<see cref="Av1"/>, the six thumbnails and
/// <see cref="Preview"/>). The clip records its current version (<c>clip.video_version</c>); a trim writes the next
/// one, and the old one is deleted once the clip moves to it.</item>
/// </list>
/// The worker never builds these: it is handed presigned URLs at claim time (docs/video-pipeline/workers-and-jobs.md).
/// </summary>
public static class R2ObjectKeys
{
    public const string Av1 = "av1.mp4";
    public const string Thumbnail = "thumbnail.jpg";
    public const string Preview = "preview.mp4";

    /// <summary>
    /// Outputs are written once per version and never change, so the CDN and browsers may keep them for good. The
    /// presigned PUTs sign this, so the worker must send exactly it.
    /// </summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>The main thumbnail and the five more legend detection reads, in that order.</summary>
    public static readonly IReadOnlyList<string> Thumbnails =
        [Thumbnail, .. Enumerable.Range(1, 5).Select(index => $"thumbnail_{index}.jpg")];

    public static string Upload(Guid clipId) => $"uploads/{clipId}";

    /// <summary>Everything a clip has under <c>clips/</c>, every version included.</summary>
    public static string ClipPrefix(Guid clipId) => $"clips/{clipId}/";

    /// <summary>Version <paramref name="version"/>'s folder, without a trailing slash.</summary>
    public static string VersionFolder(Guid clipId, int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        return $"clips/{clipId}/v{version}";
    }

    public static string Output(Guid clipId, int version, string fileName) => $"{VersionFolder(clipId, version)}/{fileName}";

    /// <summary>Every file a version can have, for deleting it.</summary>
    public static IReadOnlyList<string> AllOutputs { get; } = [Av1, .. Thumbnails, Preview];

    /// <summary>The files a job of <paramref name="jobType"/> writes, which is what its worker gets a PUT URL for.</summary>
    public static IReadOnlyList<string> OutputsFor(string jobType) => jobType switch
    {
        TranscodeRunTypes.Transcode => AllOutputs,
        TranscodeRunTypes.Thumbnails => Thumbnails,
        TranscodeRunTypes.Trim => [Av1, .. Thumbnails],
        _ => []
    };

    /// <summary>The <c>Content-Type</c> an output is stored with, which its presigned PUT signs.</summary>
    public static string ContentType(string fileName) => Path.GetExtension(fileName) switch
    {
        ".mp4" => "video/mp4",
        ".jpg" => "image/jpeg",
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "Not an output file name")
    };
}
