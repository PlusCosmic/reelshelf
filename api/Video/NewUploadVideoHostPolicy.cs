namespace Reelshelf.Video;

/// <summary>
/// Which host a new clip's video goes to. <c>VideoHost:NewUploads</c> names the host (Bunny when unset) and
/// <c>VideoHost:Users</c> lists the account ids that get it; everyone else, and everyone when the list is empty,
/// stays on Bunny. Existing clips keep the host they were created on.
/// </summary>
public sealed class NewUploadVideoHostPolicy
{
    public const string SectionName = "VideoHost";

    private readonly HashSet<Guid> _users;

    public NewUploadVideoHostPolicy(string newUploads, IEnumerable<Guid> users)
    {
        NewUploads = newUploads;
        _users = users.ToHashSet();
    }

    /// <summary>The host new uploads by the listed users go to.</summary>
    public string NewUploads { get; }

    /// <summary>The host for a new clip uploaded by <paramref name="ownerId"/>.</summary>
    public string HostFor(Guid ownerId)
    {
        return _users.Contains(ownerId) ? NewUploads : VideoHostNames.Bunny;
    }

    /// <summary>
    /// Reads the policy from configuration. Throws on a malformed account id or a host name that is not one of
    /// <see cref="VideoHostNames"/>; whether that host is registered is checked by
    /// <see cref="VideoHostRegistration.AddClipVideoHostRouting"/>.
    /// </summary>
    public static NewUploadVideoHostPolicy FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        string? configuredHost = section["NewUploads"];
        string newUploads = string.IsNullOrWhiteSpace(configuredHost)
            ? VideoHostNames.Bunny
            : configuredHost.Trim().ToLowerInvariant();
        if (!VideoHostNames.All.Contains(newUploads))
        {
            throw new InvalidOperationException(
                $"VideoHost:NewUploads is '{configuredHost}', which is not a video host ({string.Join(", ", VideoHostNames.All)})");
        }

        List<Guid> users = [];
        foreach (string? value in section.GetSection("Users").Get<string?[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Guid.TryParse(value, out Guid userId))
            {
                throw new InvalidOperationException($"VideoHost:Users has '{value}', which is not an account id");
            }

            users.Add(userId);
        }

        return new NewUploadVideoHostPolicy(newUploads, users);
    }
}
