using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Reelshelf.Devices;

/// <summary>
/// The "DeviceToken" authentication scheme (ADR-0010): a desktop uploader's <c>Authorization: Bearer rsd_…</c>,
/// hashed and looked up among tokens that aren't revoked and whose account isn't suspended or being deleted. Its
/// principal carries the account id as <see cref="ClaimTypes.NameIdentifier"/>, like the cookie's, so the account's
/// role and plan apply unchanged, and the token's id as <see cref="DeviceIdClaim"/>. It is used only by
/// <see cref="Auth.AuthorizationPolicies.CookieOrDevice"/> and <see cref="Auth.AuthorizationPolicies.Device"/>, so a
/// token works only on the endpoints that opt in. It never reads a token from a query string or a cookie.
/// </summary>
public sealed class DeviceTokenAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<DeviceOptions> deviceOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceToken";

    /// <summary>The id of the <c>device_token</c> row the request authenticated with.</summary>
    public const string DeviceIdClaim = "device_id";

    /// <summary>The header naming the client and its version, such as <c>uploader/1.0.3</c>.</summary>
    public const string ClientHeader = "X-Reelshelf-Client";

    private const int MaxClientVersionLength = 64;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string token = header["Bearer ".Length..].Trim();
        if (!DeviceCodes.LooksLikeToken(token))
        {
            // Another scheme's bearer (a worker secret, a room token) isn't this scheme's to refuse.
            return AuthenticateResult.NoResult();
        }

        DeviceStatements statements = Context.RequestServices.GetRequiredService<DeviceStatements>();
        DeviceTokenRow? row = await statements.FindToken(DeviceCodes.Hash(token));
        if (row is null)
        {
            return AuthenticateResult.Fail("Unknown or revoked device token");
        }

        string? clientVersion = ClientVersion(Request.Headers[ClientHeader].ToString());
        if (row.LastUsedAt < DateTimeOffset.UtcNow - deviceOptions.CurrentValue.LastUsedPrecision)
        {
            await statements.MarkUsed(row.Id, clientVersion ?? row.ClientVersion);
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, row.AppUserId.ToString()),
                new Claim(DeviceIdClaim, row.Id.ToString())
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    /// <summary>The device a request authenticated as, or null for the cookie.</summary>
    public static Guid? DeviceId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(DeviceIdClaim)?.Value, out Guid id) ? id : null;

    private static string? ClientVersion(string header)
    {
        string trimmed = header.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, MaxClientVersionLength)];
    }
}
