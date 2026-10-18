using System.Text;

namespace Reelshelf.Video.R2;

/// <summary>
/// Downloads of R2 clips: a presigned GET for the current version's <c>av1.mp4</c> that R2 answers with
/// <c>Content-Disposition: attachment</c>, so the browser saves the file straight from the bucket and the API server
/// does no work. Presigning signs with the key alone, so this makes no call to R2.
/// </summary>
public sealed class R2Downloads(R2Storage storage) : IClipVideoDownloads
{
    /// <summary>
    /// Long enough to follow the redirect and start the transfer (a started GET isn't cut off at expiry), short
    /// enough that a copied link soon stops working.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string DownloadUrl(ClipVideoRef video, string fileName) =>
        storage.PresignGet(R2ObjectKeys.Output(video.VideoId, video.VideoVersion, R2ObjectKeys.Av1), Lifetime,
            AttachmentDisposition(fileName));

    /// <summary>
    /// <c>attachment</c> with a quoted ASCII <c>filename</c> (anything else becomes <c>_</c>) for old clients and the
    /// exact name as RFC 5987's <c>filename*=UTF-8''...</c>, which browsers prefer, since titles can hold any
    /// character.
    /// </summary>
    public static string AttachmentDisposition(string fileName)
    {
        StringBuilder ascii = new(fileName.Length);
        foreach (char character in fileName)
        {
            ascii.Append(character is >= ' ' and <= '~' and not '"' and not '\\' ? character : '_');
        }

        // EscapeDataString leaves only RFC 3986's unreserved characters as they are, all of which are RFC 5987
        // attr-chars, and percent-encodes the UTF-8 of everything else.
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
