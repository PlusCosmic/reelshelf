using Microsoft.Extensions.Options;

namespace Reelshelf.Devices;

/// <summary>Answers <c>POST /api/devices/authorize</c>: what the app shows and polls with.</summary>
public sealed record DeviceAuthorizationResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int Interval,
    int ExpiresIn);

/// <param name="Client">
/// The app signing in: <c>uploader</c> (the default, when left out) or <c>monitor</c>, whose token alone reaches the
/// admin endpoints the monitor app reads.
/// </param>
public sealed record DeviceAuthorizeRequest(string DeviceName, string? Client = null);

public sealed record DeviceTokenRequest(string DeviceCode);

/// <summary>Answers <c>POST /api/devices/token</c> once the request is approved.</summary>
public sealed record DeviceTokenResponse(string AccessToken, string TokenType);

/// <summary>An RFC 8628 error from <c>POST /api/devices/token</c>, as a 400.</summary>
public sealed record DeviceTokenError(string Error);

/// <summary>
/// Device sign-in, modelled on the OAuth device authorization grant (RFC 8628): the app asks for a sign-in, its owner
/// approves it on <c>/device</c>, and the app polls until it gets a token.
/// </summary>
public sealed class DeviceSignInService(
    DeviceStatements statements,
    IOptionsMonitor<DeviceOptions> options,
    IConfiguration configuration)
{
    public const int MaxDeviceNameLength = 64;

    private const int MaxUserCodeAttempts = 8;

    /// <summary>A device name as stored, trimmed, or null when it is empty or too long.</summary>
    public static string? NormalizeDeviceName(string? name)
    {
        string trimmed = name?.Trim() ?? "";
        return trimmed.Length is 0 or > MaxDeviceNameLength ? null : trimmed;
    }

    public async Task<DeviceAuthorizationResponse> Authorize(string deviceName, string client)
    {
        DeviceOptions current = options.CurrentValue;
        string deviceCode = DeviceCodes.NewDeviceCode();
        byte[] deviceCodeHash = DeviceCodes.Hash(deviceCode);
        for (int attempt = 0; attempt < MaxUserCodeAttempts; attempt++)
        {
            string userCode = DeviceCodes.NewUserCode();
            if (!await statements.TryCreateAuthorization(Guid.NewGuid(), deviceCodeHash, userCode, deviceName, client,
                    current.CodeLifetime))
            {
                continue;
            }

            string shown = DeviceCodes.FormatUserCode(userCode);
            string verificationUri = $"{FrontendOrigin()}/device";
            return new DeviceAuthorizationResponse(
                deviceCode,
                shown,
                verificationUri,
                $"{verificationUri}?code={shown}",
                (int)Math.Ceiling(current.PollInterval.TotalSeconds),
                (int)Math.Ceiling(current.CodeLifetime.TotalSeconds));
        }

        // 20^8 codes against a few live ones: reaching here means something else is wrong.
        throw new InvalidOperationException("Couldn't find a free user code for a device sign-in");
    }

    /// <summary>One poll for a token: the token once approved, else the RFC 8628 error to answer.</summary>
    public async Task<(DeviceTokenResponse? Token, string? Error)> Poll(string? deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return (null, "expired_token");
        }

        string token = DeviceCodes.NewToken();
        (DeviceTokenPollOutcome outcome, _) = await statements.Poll(
            DeviceCodes.Hash(deviceCode.Trim()), DeviceCodes.Hash(token), options.CurrentValue.PollInterval);
        return outcome switch
        {
            DeviceTokenPollOutcome.Issued => (new DeviceTokenResponse(token, "Bearer"), null),
            DeviceTokenPollOutcome.AuthorizationPending => (null, "authorization_pending"),
            DeviceTokenPollOutcome.SlowDown => (null, "slow_down"),
            DeviceTokenPollOutcome.AccessDenied => (null, "access_denied"),
            _ => (null, "expired_token")
        };
    }

    private string FrontendOrigin() =>
        (configuration["FrontendOrigin"] ?? "http://localhost:5173").TrimEnd('/');
}
