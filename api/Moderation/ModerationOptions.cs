namespace Reelshelf.Moderation;

/// <summary><c>Moderation:*</c>: reports, takedowns, and the evidence kept for clips removed after a report.</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>Where an owner disputes a takedown.</summary>
    public string ContactEmail { get; set; } = "support@reelshelf.app";

    /// <summary>
    /// The private R2 bucket removed clips are copied into, in the same account and reached with the same <c>R2:*</c>
    /// credentials as the media bucket. It must have no public domain. Without it, clips can't be removed.
    /// </summary>
    public string EvidenceBucket { get; set; } = "";

    /// <summary>How long a removed clip's evidence is kept before it is deleted (NCMEC asks for a year).</summary>
    public TimeSpan EvidenceRetention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>A claimed evidence copy with no progress for this long is claimed again.</summary>
    public TimeSpan EvidenceStaleAfter { get; set; } = TimeSpan.FromMinutes(30);

    public int EvidenceMaxAttempts { get; set; } = 5;

    public TimeSpan EvidenceRetryAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When the second sweep of a removed clip's media folder runs. A transcode worker may still upload into it with
    /// presigned URLs it held at removal, so this must exceed <c>Transcoding:PresignLifetime</c> (2 hours).
    /// </summary>
    public TimeSpan SweepAfter { get; set; } = TimeSpan.FromHours(3);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public CloudflareOptions Cloudflare { get; set; } = new();

    public sealed class CloudflareOptions
    {
        /// <summary>The zone of the media domain (<c>R2:MediaBaseUrl</c>).</summary>
        public string ZoneId { get; set; } = "";

        /// <summary>An API token with only the zone's Cache Purge permission.</summary>
        public string ApiToken { get; set; } = "";

        public bool IsConfigured => ZoneId.Length > 0 && ApiToken.Length > 0;
    }
}
