using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Reelshelf.Video.Bunny;

/// <summary>
/// Bunny Stream's presigned TUS upload: the browser uploads straight to Bunny, authorised by a short-lived signature
/// over the library, access key, expiry and video rather than by the access key itself.
/// </summary>
public static class BunnyUploadTarget
{
    public const string Endpoint = "https://video.bunnycdn.com/tusupload";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static UploadTarget Create(string libraryId, string accessKey, HostedVideo video, string title, DateTimeOffset now)
    {
        long expiration = (now + Lifetime).ToUnixTimeSeconds();
        return new UploadTarget(
            UploadTarget.Tus,
            Endpoint,
            new Dictionary<string, string>
            {
                ["AuthorizationSignature"] = Sign(libraryId, accessKey, expiration, video.VideoId),
                ["AuthorizationExpire"] = expiration.ToString(CultureInfo.InvariantCulture),
                ["VideoId"] = video.VideoId.ToString(),
                ["LibraryId"] = libraryId,
            },
            new Dictionary<string, string>
            {
                ["title"] = title,
                ["collection"] = video.ContainerId.ToString(),
            },
            DateTimeOffset.FromUnixTimeSeconds(expiration));
    }

    /// <summary>SHA-256 of library id + access key + expiry (unix seconds) + video id, as upper-case hex.</summary>
    public static string Sign(string libraryId, string accessKey, long expiration, Guid videoId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(libraryId + accessKey + expiration + videoId));
        return Convert.ToHexString(hash);
    }
}
