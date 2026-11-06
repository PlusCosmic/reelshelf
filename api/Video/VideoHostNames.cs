namespace Reelshelf.Video;

/// <summary>
/// The names video hosts are registered under, stored in <c>clip.video_host</c> to say which host holds a clip's
/// video. The column's check constraint lists the same values, so a new host needs a migration adding its name
/// before it is registered.
/// </summary>
public static class VideoHostNames
{
    /// <summary>Our own pipeline: Cloudflare R2 storage with our own transcodes.</summary>
    public const string R2 = "r2";

    public static readonly IReadOnlyList<string> All = [R2];
}
