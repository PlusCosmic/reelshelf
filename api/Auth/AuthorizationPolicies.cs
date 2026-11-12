using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Reelshelf.Devices;

namespace Reelshelf.Auth;

/// <summary>
/// Named authorization policies beyond the default one (the site cookie). An endpoint's policies combine, so adding one
/// of these beside a group's <c>RequireAuthorization()</c> widens the schemes the endpoint accepts.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// The site cookie or a device token (ADR-0010). Only the endpoints the desktop uploader and the monitor app need
    /// carry it: listing books, the plan, preparing a clip, deleting one still awaiting its upload, the multipart upload
    /// endpoints, <c>GET /api/me</c> and <c>/api/me/features</c>, and the admin accounts, reports and ops endpoints
    /// (whose permissions keep them to Admins). Every other endpoint sees the cookie alone, so a device token gets 401
    /// there.
    /// </summary>
    public const string CookieOrDevice = "CookieOrDevice";

    /// <summary>A device token alone, for the uploader signing itself out.</summary>
    public const string Device = "Device";

    public static void AddDevicePolicies(this AuthorizationBuilder authorization)
    {
        authorization
            .AddPolicy(CookieOrDevice, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme,
                    DeviceTokenAuthentication.SchemeName)
                .RequireAuthenticatedUser())
            .AddPolicy(Device, policy => policy
                .AddAuthenticationSchemes(DeviceTokenAuthentication.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DeviceTokenAuthentication.DeviceIdClaim));
    }
}
