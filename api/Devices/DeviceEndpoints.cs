using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.FeatureFlags;

namespace Reelshelf.Devices;

/// <summary>
/// Device sign-in for the desktop uploader (ADR-0010) and the account's list of devices. The cookie endpoints sit
/// behind the <c>desktop-uploader</c> flag; the anonymous ones can't know the account, so the flag is checked when the
/// request is approved. Signing a device out with its own token is never flagged, so an issued token can always be
/// given up.
/// </summary>
public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder devices = app.MapGroup("devices");

        devices.MapPost("authorize", Authorize).WithName("AuthorizeDevice")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.DeviceAuthorize);
        devices.MapGet("authorize/{userCode}", GetAuthorization).WithName("GetDeviceAuthorization")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate)
            .RequireFeature(FeatureFlagKeys.DesktopUploader);
        devices.MapPost("authorize/{userCode}/approve", Approve).WithName("ApproveDevice")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate)
            .RequireFeature(FeatureFlagKeys.DesktopUploader);
        devices.MapPost("authorize/{userCode}/deny", Deny).WithName("DenyDevice")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate)
            .RequireFeature(FeatureFlagKeys.DesktopUploader);
        devices.MapPost("token", Token).WithName("GetDeviceToken")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.DeviceToken);
        devices.MapDelete("current", SignOutCurrent).WithName("SignOutCurrentDevice")
            .RequireAuthorization(AuthorizationPolicies.Device);

        RouteGroupBuilder me = app.MapGroup("me/devices");
        me.MapGet("", ListDevices).WithName("GetMyDevices")
            .RequireAuthorization()
            .RequireFeature(FeatureFlagKeys.DesktopUploader);
        me.MapDelete("{deviceId:guid}", RemoveDevice).WithName("RemoveMyDevice")
            .RequireAuthorization()
            .RequireFeature(FeatureFlagKeys.DesktopUploader);
    }

    /// <summary>Starts a sign-in for a device: the codes to show and poll with.</summary>
    private static async Task<Results<Ok<DeviceAuthorizationResponse>, BadRequest<string>>> Authorize(
        DeviceSignInService signIn,
        DeviceAuthorizeRequest request)
    {
        string? name = DeviceSignInService.NormalizeDeviceName(request.DeviceName);
        if (name is null)
        {
            return TypedResults.BadRequest(
                $"Give the device a name of 1 to {DeviceSignInService.MaxDeviceNameLength} characters.");
        }

        if (DeviceClients.Normalize(request.Client) is not { } client)
        {
            return TypedResults.BadRequest(
                $"The client must be one of {string.Join(", ", DeviceClients.All)}, or left out for the uploader.");
        }

        return TypedResults.Ok(await signIn.Authorize(name, client));
    }

    /// <summary>The sign-in request <c>/device</c> asks about, while it is waiting for an answer.</summary>
    private static async Task<Results<Ok<DeviceAuthorizationRequest>, NotFound>> GetAuthorization(
        DeviceStatements statements,
        string userCode)
    {
        return DeviceCodes.NormalizeUserCode(userCode) is { } code &&
               await statements.GetPendingAuthorization(code) is { } request
            ? TypedResults.Ok(request)
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound, Conflict>> Approve(
        DeviceStatements statements,
        AuthenticatedUser user,
        string userCode)
    {
        return await Decide(statements, userCode, user.Id);
    }

    private static async Task<Results<NoContent, NotFound, Conflict>> Deny(
        DeviceStatements statements,
        string userCode)
    {
        return await Decide(statements, userCode, approvedBy: null);
    }

    private static async Task<Results<NoContent, NotFound, Conflict>> Decide(
        DeviceStatements statements, string userCode, Guid? approvedBy)
    {
        if (DeviceCodes.NormalizeUserCode(userCode) is not { } code)
        {
            return TypedResults.NotFound();
        }

        return await statements.Decide(code, approvedBy) switch
        {
            DeviceDecisionOutcome.Decided => TypedResults.NoContent(),
            DeviceDecisionOutcome.AlreadyDecided => TypedResults.Conflict(),
            _ => TypedResults.NotFound()
        };
    }

    /// <summary>
    /// The app's poll: the token once the request is approved, else an RFC 8628 error (<c>authorization_pending</c>,
    /// <c>slow_down</c>, <c>access_denied</c> or <c>expired_token</c>) as a 400.
    /// </summary>
    private static async Task<Results<Ok<DeviceTokenResponse>, BadRequest<DeviceTokenError>>> Token(
        DeviceSignInService signIn,
        DeviceTokenRequest request,
        HttpContext context)
    {
        (DeviceTokenResponse? token, string? error) = await signIn.Poll(request.DeviceCode);
        // RFC 8628 (via RFC 6749 section 5.1): token responses are never cached.
        context.Response.Headers.CacheControl = "no-store";
        return token is not null
            ? TypedResults.Ok(token)
            : TypedResults.BadRequest(new DeviceTokenError(error ?? "expired_token"));
    }

    /// <summary>The uploader's Sign out: revokes the token the request carries.</summary>
    private static async Task<NoContent> SignOutCurrent(
        DeviceStatements statements,
        AuthenticatedUser user,
        ClaimsPrincipal principal)
    {
        if (DeviceTokenAuthentication.DeviceId(principal) is { } deviceId)
        {
            await statements.Revoke(deviceId, user.Id);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<DeviceSummary>>> ListDevices(
        DeviceStatements statements,
        AuthenticatedUser user)
    {
        return TypedResults.Ok(await statements.ListDevices(user.Id));
    }

    private static async Task<Results<NoContent, NotFound>> RemoveDevice(
        DeviceStatements statements,
        AuthenticatedUser user,
        Guid deviceId)
    {
        return await statements.Revoke(deviceId, user.Id) ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
