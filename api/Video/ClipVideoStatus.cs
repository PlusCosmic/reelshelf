using System.Text.Json.Serialization;

namespace Reelshelf.Video;

/// <summary>
/// Where a clip's video is on its way to playable, in the app's own terms. Stored as text in <c>clip.video_state</c>
/// and serialized with the same spelling; the video host maps its own statuses onto these at the edge.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClipVideoStatus>))]
public enum ClipVideoStatus
{
    /// <summary>Reserved or created at the host, with none of its bytes received yet.</summary>
    [JsonStringEnumMemberName("awaiting_upload")]
    AwaitingUpload,

    /// <summary>The host has the bytes and has not started processing them.</summary>
    [JsonStringEnumMemberName("uploaded")]
    Uploaded,

    /// <summary>The host is encoding the video.</summary>
    [JsonStringEnumMemberName("processing")]
    Processing,

    /// <summary>Encoded and playable.</summary>
    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>The host received the bytes but could not encode them.</summary>
    [JsonStringEnumMemberName("encode_failed")]
    EncodeFailed,

    /// <summary>The upload to the host failed.</summary>
    [JsonStringEnumMemberName("upload_failed")]
    UploadFailed
}

public static class ClipVideoStatuses
{
    /// <summary>The <c>clip.video_state</c> spelling, which is also the JSON one.</summary>
    public static string ToDatabase(this ClipVideoStatus status) => status switch
    {
        ClipVideoStatus.AwaitingUpload => "awaiting_upload",
        ClipVideoStatus.Uploaded => "uploaded",
        ClipVideoStatus.Processing => "processing",
        ClipVideoStatus.Ready => "ready",
        ClipVideoStatus.EncodeFailed => "encode_failed",
        ClipVideoStatus.UploadFailed => "upload_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    /// <summary>Reads <c>clip.video_state</c>, which the column's check constraint keeps to the values above.</summary>
    public static ClipVideoStatus FromDatabase(string value) => value switch
    {
        "awaiting_upload" => ClipVideoStatus.AwaitingUpload,
        "uploaded" => ClipVideoStatus.Uploaded,
        "processing" => ClipVideoStatus.Processing,
        "ready" => ClipVideoStatus.Ready,
        "encode_failed" => ClipVideoStatus.EncodeFailed,
        "upload_failed" => ClipVideoStatus.UploadFailed,
        _ => throw new InvalidOperationException($"Unknown clip video state '{value}'")
    };

    /// <summary>A clip plays, shares and is transcribed only once its video is ready.</summary>
    public static bool IsPlayable(this ClipVideoStatus status) => status == ClipVideoStatus.Ready;

    /// <summary>
    /// True for statuses that mean bytes reached the host and are being (or have been) processed. A failed encode
    /// is deliberately excluded: one with zero storage is abandoned, and one with storage is kept by the storage
    /// check that runs before this.
    /// </summary>
    public static bool IsUploaded(this ClipVideoStatus status) =>
        status is not (ClipVideoStatus.AwaitingUpload or ClipVideoStatus.UploadFailed or ClipVideoStatus.EncodeFailed);
}
