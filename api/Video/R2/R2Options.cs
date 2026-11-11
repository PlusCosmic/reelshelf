using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Reelshelf.Video.R2;

/// <summary>
/// The object store behind our own pipeline: an S3 bucket reached through the S3 API, either AWS S3 itself in
/// <see cref="Region"/> or an S3-compatible service at <see cref="ServiceUrl"/>, such as Cloudflare R2. It is read from
/// <c>Storage:*</c>, or from the older <c>R2:*</c> spelling, which names an R2 account. Dev and prod each have their own
/// bucket. Media is served from <see cref="MediaBaseUrl"/>, a custom domain on a CDN (never <c>r2.dev</c>, which is
/// uncached and rate-limited).
/// </summary>
public sealed partial class R2Options
{
    public const string SectionName = "Storage";
    public const string LegacySectionName = "R2";

    /// <summary>The region R2 and other S3-compatible endpoints are signed for when none is given.</summary>
    public const string DefaultEndpointRegion = "auto";

    /// <summary>The R2 account, from <c>R2:AccountId</c>; its S3 endpoint is the <see cref="ServiceUrl"/>.</summary>
    public string AccountId { get; init; } = "";

    /// <summary>An S3-compatible endpoint's https URL (<c>Storage:Endpoint</c>); empty for AWS S3.</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>
    /// The AWS region of the bucket, or the region an endpoint is signed for; empty means <c>auto</c> there.
    /// </summary>
    public string Region { get; init; } = "";

    public string AccessKeyId { get; init; } = "";
    public string SecretAccessKey { get; init; } = "";
    public string Bucket { get; init; } = "";

    /// <summary>The media domain with its scheme and no trailing slash, e.g. <c>https://media-dev.reelshelf.app</c>.</summary>
    public string MediaBaseUrl { get; init; } = "";

    /// <summary>
    /// The S3-compatible endpoint the client calls, addressed path-style: <see cref="Endpoint"/>, else R2's endpoint
    /// for <see cref="AccountId"/>. Null for AWS S3, which the SDK reaches from <see cref="Region"/> alone.
    /// </summary>
    public string? ServiceUrl =>
        Endpoint.Length > 0 ? Endpoint
        : AccountId.Length > 0 ? $"https://{AccountId}.r2.cloudflarestorage.com"
        : null;

    /// <summary>The region requests are signed for.</summary>
    public string SigningRegion => Region.Length > 0 ? Region : DefaultEndpointRegion;

    /// <summary>The same store and credentials with another bucket, such as the evidence bucket.</summary>
    public R2Options WithBucket(string bucket) => new()
    {
        AccountId = AccountId,
        Endpoint = Endpoint,
        Region = Region,
        AccessKeyId = AccessKeyId,
        SecretAccessKey = SecretAccessKey,
        Bucket = bucket,
        MediaBaseUrl = MediaBaseUrl
    };

    private static readonly string[] StorageKeys =
        [nameof(Endpoint), nameof(Region), nameof(AccessKeyId), nameof(SecretAccessKey), nameof(Bucket), nameof(MediaBaseUrl)];

    private static readonly string[] LegacyKeys =
        [nameof(AccountId), nameof(AccessKeyId), nameof(SecretAccessKey), nameof(Bucket), nameof(MediaBaseUrl)];

    /// <summary>
    /// Reads <c>Storage:*</c> or <c>R2:*</c>. Returns null when neither is set, so the <c>r2</c> host is simply not
    /// registered; throws when both are, when only some of one is, or when the media URL or endpoint isn't an https
    /// origin, so a half-configured deployment fails at startup rather than on its first upload. Only the object store's
    /// own keys count: <c>Storage</c> also holds the plan's <c>BackstopBytes</c> and <c>WarnAtPercent</c>.
    /// </summary>
    public static R2Options? FromConfiguration(IConfiguration configuration)
    {
        Dictionary<string, string> storage = Read(configuration, SectionName, StorageKeys);
        Dictionary<string, string> legacy = Read(configuration, LegacySectionName, LegacyKeys);
        bool hasStorage = storage.Values.Any(value => value.Length > 0);
        bool hasLegacy = legacy.Values.Any(value => value.Length > 0);

        if (hasStorage && hasLegacy)
        {
            throw new InvalidOperationException(
                $"Object storage is configured twice: set {SectionName}:* or the older {LegacySectionName}:*, not both " +
                $"(found {string.Join(", ", Present(SectionName, storage).Concat(Present(LegacySectionName, legacy)))})");
        }

        if (hasStorage)
        {
            return FromStorage(storage);
        }

        return hasLegacy ? FromLegacy(legacy) : null;
    }

    private static R2Options FromStorage(Dictionary<string, string> values)
    {
        string endpoint = values[nameof(Endpoint)];
        IEnumerable<string> required = StorageKeys.Where(key =>
            key != nameof(Endpoint) && (key != nameof(Region) || endpoint.Length == 0));
        RequireAll(SectionName, values, required);

        if (endpoint.Length == 0 && !AwsRegionPattern().IsMatch(values[nameof(Region)]))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Region is '{values[nameof(Region)]}'; it must be an AWS region such as eu-north-1");
        }

        if (endpoint.Length > 0)
        {
            endpoint = endpoint.TrimEnd('/');
            if (!IsHttpsOrigin(endpoint, out _))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Endpoint is '{values[nameof(Endpoint)]}'; it must be the https origin of an S3-compatible endpoint");
            }
        }

        return new R2Options
        {
            Endpoint = endpoint,
            Region = values[nameof(Region)],
            AccessKeyId = values[nameof(AccessKeyId)],
            SecretAccessKey = values[nameof(SecretAccessKey)],
            Bucket = values[nameof(Bucket)],
            MediaBaseUrl = MediaBase(SectionName, values[nameof(MediaBaseUrl)])
        };
    }

    private static R2Options FromLegacy(Dictionary<string, string> values)
    {
        RequireAll(LegacySectionName, values, LegacyKeys);
        return new R2Options
        {
            AccountId = values[nameof(AccountId)],
            AccessKeyId = values[nameof(AccessKeyId)],
            SecretAccessKey = values[nameof(SecretAccessKey)],
            Bucket = values[nameof(Bucket)],
            MediaBaseUrl = MediaBase(LegacySectionName, values[nameof(MediaBaseUrl)])
        };
    }

    private static Dictionary<string, string> Read(IConfiguration configuration, string sectionName, string[] keys)
    {
        IConfigurationSection section = configuration.GetSection(sectionName);
        return keys.ToDictionary(key => key, key => section[key]?.Trim() ?? "");
    }

    private static IEnumerable<string> Present(string sectionName, Dictionary<string, string> values) =>
        values.Where(pair => pair.Value.Length > 0).Select(pair => $"{sectionName}:{pair.Key}");

    private static void RequireAll(string sectionName, Dictionary<string, string> values, IEnumerable<string> keys)
    {
        List<string> missing = keys.Where(key => values[key].Length == 0).Select(key => $"{sectionName}:{key}").ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{sectionName} is partly configured: {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} missing");
        }
    }

    private static string MediaBase(string sectionName, string configured)
    {
        string mediaBaseUrl = configured.TrimEnd('/');
        if (!IsHttpsOrigin(mediaBaseUrl, out Uri? media) ||
            media.Host.EndsWith(".r2.dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{sectionName}:MediaBaseUrl is '{configured}'; it must be the https origin of the media domain, not r2.dev");
        }

        return mediaBaseUrl;
    }

    private static bool IsHttpsOrigin(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.PathAndQuery == "/";

    [GeneratedRegex("^[a-z]{2}(-[a-z]+)+-[0-9]+$")]
    private static partial Regex AwsRegionPattern();
}
