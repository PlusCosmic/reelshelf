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

/// <summary>
/// What is moving a clip's <c>clip.video_state</c>. Each writer in <see cref="Core.ClipsStatements"/> is one of these,
/// and <see cref="ClipVideoTransitions"/> says which states each may leave for which.
/// </summary>
public enum ClipVideoChange
{
    /// <summary>A reserved clip gets the video its host created for it (<c>AttachVideo</c>).</summary>
    Attach,

    /// <summary>The source reached R2, from the Twitch import or a browser upload's completion (<c>SetSourceUploaded</c>).</summary>
    SourceUploaded,

    /// <summary>An upload into R2 failed or its file was rejected (<c>SetUploadFailed</c>).</summary>
    UploadFailed,

    /// <summary>Our worker's progress on a <c>transcode</c> (<c>SetTranscodeProgress</c>).</summary>
    TranscodeProgress,

    /// <summary>A shutting-down worker handed a <c>transcode</c> back (<c>SetTranscodeReleased</c>).</summary>
    TranscodeReleased,

    /// <summary>A <c>transcode</c> failed for good (<c>SetTranscodeFailed</c>).</summary>
    TranscodeFailed,

    /// <summary>A <c>transcode</c> or <c>trim</c> finished (<c>SetTranscodeReady</c>).</summary>
    TranscodeReady
}

/// <summary>
/// The one list of the moves <c>clip.video_state</c> may make. Every statement that writes the column (but the two job
/// completions, below) takes its allowed from-states from <see cref="FromStates"/> and puts them in its <c>WHERE</c>,
/// so a report that arrives late or out of order changes nothing (none of the row's columns, not only the state)
/// instead of moving the clip back.
/// <list type="bullet">
/// <item><see cref="ClipVideoChange.Attach"/>: only a reserved clip (<c>awaiting_upload</c>), to whatever the host reports.</item>
/// <item><see cref="ClipVideoChange.SourceUploaded"/> and <see cref="ClipVideoChange.UploadFailed"/>: only from
/// <c>awaiting_upload</c>. <c>upload_failed</c> is final for an R2 clip: its file was rejected and deleted (or the
/// Twitch import deleted the clip), and the uploader prepares a new clip for another try.</item>
/// <item><see cref="ClipVideoChange.TranscodeProgress"/>: <c>uploaded</c> or <c>processing</c> → <c>processing</c>.</item>
/// <item><see cref="ClipVideoChange.TranscodeReleased"/>: <c>processing</c> → <c>uploaded</c>.</item>
/// <item><see cref="ClipVideoChange.TranscodeFailed"/>: <c>uploaded</c> or <c>processing</c> → <c>encode_failed</c>.</item>
/// <item><see cref="ClipVideoChange.TranscodeReady"/>: any state → <c>ready</c>. A finished job's output is the
/// newest video the clip has; this is guarded instead by the job still
/// owning the clip (its host and version, #321), which the lease keeps to one job per clip.</item>
/// </list>
/// </summary>
public static class ClipVideoTransitions
{
    private static readonly ClipVideoStatus[] All = Enum.GetValues<ClipVideoStatus>();

    // (change, from, to): every from-state may move to every to-state for that change.
    private static readonly (ClipVideoChange Change, ClipVideoStatus[] From, ClipVideoStatus[] To)[] Rules =
    [
        (ClipVideoChange.Attach, [ClipVideoStatus.AwaitingUpload], All),

        (ClipVideoChange.SourceUploaded, [ClipVideoStatus.AwaitingUpload], [ClipVideoStatus.Uploaded]),
        (ClipVideoChange.UploadFailed, [ClipVideoStatus.AwaitingUpload], [ClipVideoStatus.UploadFailed]),
        (ClipVideoChange.TranscodeProgress, [ClipVideoStatus.Uploaded, ClipVideoStatus.Processing], [ClipVideoStatus.Processing]),
        (ClipVideoChange.TranscodeReleased, [ClipVideoStatus.Processing], [ClipVideoStatus.Uploaded]),
        (ClipVideoChange.TranscodeFailed, [ClipVideoStatus.Uploaded, ClipVideoStatus.Processing], [ClipVideoStatus.EncodeFailed]),
        (ClipVideoChange.TranscodeReady, All, [ClipVideoStatus.Ready])
    ];

    /// <summary>Whether <paramref name="change"/> may move a clip from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool CanMove(ClipVideoChange change, ClipVideoStatus from, ClipVideoStatus to) =>
        Rules.Any(rule => rule.Change == change && rule.From.Contains(from) && rule.To.Contains(to));

    /// <summary>
    /// The states, in their <c>clip.video_state</c> spelling, that <paramref name="change"/> may move to
    /// <paramref name="to"/>, for a statement's <c>video_state = ANY(@fromStates)</c>. Empty when the change never
    /// leads there, so the statement changes nothing.
    /// </summary>
    public static string[] FromStates(ClipVideoChange change, ClipVideoStatus to) =>
        All.Where(from => CanMove(change, from, to)).Select(from => from.ToDatabase()).ToArray();
}
