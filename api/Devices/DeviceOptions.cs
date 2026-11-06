namespace Reelshelf.Devices;

/// <summary>Device sign-in and device tokens (<c>Devices:*</c>).</summary>
public sealed class DeviceOptions
{
    public const string SectionName = "Devices";

    /// <summary>How long a sign-in request waits for its owner (<c>expires_in</c>).</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the app may poll for its token (<c>interval</c>); faster polls answer <c>slow_down</c>.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A token unused for this long is revoked by the daily sweep.</summary>
    public TimeSpan RevokeUnusedAfter { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often a token's <c>last_used_at</c> and <c>client_version</c> are written, at most.</summary>
    public TimeSpan LastUsedPrecision { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>The desktop uploader as a client of the API (<c>Uploader:*</c>).</summary>
public sealed class UploaderOptions
{
    public const string SectionName = "Uploader";

    /// <summary>
    /// The oldest uploader version the API serves. A request from an older one (<c>X-Reelshelf-Client:
    /// uploader/&lt;version&gt;</c>) answers 426. The default refuses nothing. Raise it only once the version it names
    /// has been in the Store for a week.
    /// </summary>
    public string MinimumVersion { get; set; } = "0.0";
}
