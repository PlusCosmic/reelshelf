using Microsoft.Extensions.Options;

namespace Reelshelf.Devices;

/// <summary>
/// Refuses an uploader older than <see cref="UploaderOptions.MinimumVersion"/>: every <c>/api</c> request whose
/// <c>X-Reelshelf-Client</c> is <c>uploader/&lt;version&gt;</c> below it answers 426 with
/// <c>{ error: "client_too_old", minimum_version }</c>, which the app shows as "Update the uploader". Requests without
/// the header, such as the website's, are never affected.
/// </summary>
public sealed class ClientVersionGate(RequestDelegate next, IOptionsMonitor<UploaderOptions> options)
{
    public const string UploaderPrefix = "uploader/";
    public const string TooOldError = "client_too_old";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") &&
            Minimum(options.CurrentValue) is { } minimum &&
            IsTooOld(context.Request.Headers[DeviceTokenAuthentication.ClientHeader].ToString(), minimum))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(
                new ClientTooOldResponse(TooOldError, minimum.ToString()),
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Whether the header names an uploader older than <paramref name="minimum"/>. An uploader whose version can't be
    /// read counts as the oldest there is.
    /// </summary>
    public static bool IsTooOld(string? header, Version minimum)
    {
        if (string.IsNullOrWhiteSpace(header) ||
            !header.Trim().StartsWith(UploaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string version = header.Trim()[UploaderPrefix.Length..];
        // Drop build metadata or a pre-release suffix ("1.2.0-beta", "1.2.0+abc").
        int suffix = version.IndexOfAny(['-', '+', ' ']);
        if (suffix >= 0)
        {
            version = version[..suffix];
        }

        Version parsed = Version.TryParse(version, out Version? read) ? read : new Version(0, 0);
        return Normalize(parsed) < Normalize(minimum);
    }

    private static Version? Minimum(UploaderOptions options) =>
        Version.TryParse(options.MinimumVersion, out Version? minimum) ? minimum : null;

    // Version treats an unset build or revision as lower than 0, so "1.2" < "1.2.0"; compare them as equal.
    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    public sealed record ClientTooOldResponse(string Error, string MinimumVersion);
}
