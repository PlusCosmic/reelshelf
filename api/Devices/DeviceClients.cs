namespace Reelshelf.Devices;

/// <summary>
/// The apps a device token can be for (V68's <c>client</c>): the Windows uploader, and the macOS monitor app, whose
/// token alone reaches the admin endpoints it reads (<see cref="Auth.AuthorizationPolicies.CookieOrMonitorDevice"/>).
/// </summary>
public static class DeviceClients
{
    public const string Uploader = "uploader";
    public const string Monitor = "monitor";

    public static readonly IReadOnlyList<string> All = [Uploader, Monitor];

    /// <summary>The client a sign-in asks for: the uploader when it names none, null when it names an unknown one.</summary>
    public static string? Normalize(string? client)
    {
        string trimmed = client?.Trim().ToLowerInvariant() ?? "";
        return trimmed.Length == 0 ? Uploader : All.Contains(trimmed) ? trimmed : null;
    }
}
