namespace Reelshelf.Video;

/// <summary>
/// Which host a new clip's video goes to: <c>VideoHost:NewUploads</c> (Bunny when unset), for every account.
/// Existing clips keep the host they were created on.
/// </summary>
public sealed class NewUploadVideoHostPolicy(string newUploads)
{
    public const string SectionName = "VideoHost";

    /// <summary>The host new uploads go to.</summary>
    public string NewUploads { get; } = newUploads;

    /// <summary>
    /// Reads the policy from configuration. Throws on a host name that is not one of <see cref="VideoHostNames"/>;
    /// whether that host is registered is checked by <see cref="VideoHostRegistration.AddClipVideoHostRouting"/>.
    /// </summary>
    public static NewUploadVideoHostPolicy FromConfiguration(IConfiguration configuration)
    {
        string? configuredHost = configuration.GetSection(SectionName)["NewUploads"];
        string newUploads = string.IsNullOrWhiteSpace(configuredHost)
            ? VideoHostNames.Bunny
            : configuredHost.Trim().ToLowerInvariant();
        if (!VideoHostNames.All.Contains(newUploads))
        {
            throw new InvalidOperationException(
                $"VideoHost:NewUploads is '{configuredHost}', which is not a video host ({string.Join(", ", VideoHostNames.All)})");
        }

        return new NewUploadVideoHostPolicy(newUploads);
    }
}
