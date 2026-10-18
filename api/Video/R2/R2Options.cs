namespace Reelshelf.Video.R2;

/// <summary>
/// Cloudflare R2, our own pipeline's storage (<c>R2:*</c>). Dev and prod each have their own bucket. The S3 API is
/// reached at <see cref="ServiceUrl"/> with region <c>auto</c>; media is served from <see cref="MediaBaseUrl"/>, a
/// custom domain on the Cloudflare CDN (never <c>r2.dev</c>, which is uncached and rate-limited).
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    /// <summary>The S3 region R2 expects in signatures.</summary>
    public const string Region = "auto";

    public string AccountId { get; init; } = "";
    public string AccessKeyId { get; init; } = "";
    public string SecretAccessKey { get; init; } = "";
    public string Bucket { get; init; } = "";

    /// <summary>The media domain with its scheme and no trailing slash, e.g. <c>https://media-dev.reelshelf.app</c>.</summary>
    public string MediaBaseUrl { get; init; } = "";

    /// <summary>R2's S3 endpoint for the account.</summary>
    public string ServiceUrl => $"https://{AccountId}.r2.cloudflarestorage.com";

    /// <summary>
    /// Reads <c>R2:*</c>. Returns null when none of it is set, so the <c>r2</c> host is simply not registered; throws
    /// when only some of it is, or when the media URL isn't an https URL on our own domain, so a half-configured
    /// deployment fails at startup rather than on its first R2 upload.
    /// </summary>
    public static R2Options? FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        Dictionary<string, string> values = new[]
            {
                nameof(AccountId), nameof(AccessKeyId), nameof(SecretAccessKey), nameof(Bucket), nameof(MediaBaseUrl)
            }
            .ToDictionary(key => key, key => section[key]?.Trim() ?? "");

        List<string> missing = values.Where(pair => pair.Value.Length == 0).Select(pair => $"R2:{pair.Key}").ToList();
        if (missing.Count == values.Count)
        {
            return null;
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"R2 is partly configured: {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} missing");
        }

        string mediaBaseUrl = values[nameof(MediaBaseUrl)].TrimEnd('/');
        if (!Uri.TryCreate(mediaBaseUrl, UriKind.Absolute, out Uri? media) || media.Scheme != Uri.UriSchemeHttps ||
            media.Host.EndsWith(".r2.dev", StringComparison.OrdinalIgnoreCase) || media.PathAndQuery != "/")
        {
            throw new InvalidOperationException(
                $"R2:MediaBaseUrl is '{values[nameof(MediaBaseUrl)]}'; it must be the https origin of the media domain, not r2.dev");
        }

        return new R2Options
        {
            AccountId = values[nameof(AccountId)],
            AccessKeyId = values[nameof(AccessKeyId)],
            SecretAccessKey = values[nameof(SecretAccessKey)],
            Bucket = values[nameof(Bucket)],
            MediaBaseUrl = mediaBaseUrl
        };
    }
}
