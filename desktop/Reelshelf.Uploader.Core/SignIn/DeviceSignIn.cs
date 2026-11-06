using System.Net;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.ApiClient.Models;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Core.SignIn;

/// <summary>A sign-in waiting for its owner on reelshelf.app/device (S1).</summary>
public sealed record PendingSignIn(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    Uri VerificationUriComplete,
    TimeSpan Interval,
    DateTimeOffset ExpiresAt);

public enum SignInOutcome
{
    /// <summary>A code was issued; the sign-in waits for its owner.</summary>
    Started,
    SignedIn,
    Denied,
    Expired,
    Unreachable,
    UpdateRequired
}

public sealed record SignInResult(SignInOutcome Outcome, PendingSignIn? Pending = null);

/// <summary>
/// Signing the app in (ADR-0010, RFC 8628): ask for a code, open reelshelf.app/device on it, and poll at the interval
/// the API gives (5 seconds more after each <c>slow_down</c>) until the owner allows or turns it down, or the code
/// expires. The token goes to the credential store, never anywhere else.
/// </summary>
public sealed class DeviceSignIn(UploaderApi api, ICredentialStore credentials, IClock clock)
{
    public static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    public async Task<SignInResult> StartAsync(string deviceName, CancellationToken cancellationToken)
    {
        ApiResult<DeviceAuthorizationResponse> started = await api.AuthorizeAsync(deviceName, cancellationToken);
        if (started.Status == HttpStatusCode.UpgradeRequired)
        {
            return new SignInResult(SignInOutcome.UpdateRequired);
        }

        if (!started.IsSuccess || started.Value is not { DeviceCode: { } deviceCode, UserCode: { } userCode } value ||
            !Uri.TryCreate(value.VerificationUri, UriKind.Absolute, out Uri? verification) ||
            !Uri.TryCreate(value.VerificationUriComplete, UriKind.Absolute, out Uri? complete))
        {
            return new SignInResult(SignInOutcome.Unreachable);
        }

        PendingSignIn pending = new(deviceCode, userCode, verification, complete,
            TimeSpan.FromSeconds(Math.Max(1, value.Interval ?? 5)),
            clock.UtcNow + TimeSpan.FromSeconds(value.ExpiresIn ?? 600));
        return new SignInResult(SignInOutcome.Started, pending);
    }

    /// <summary>Polls until the sign-in is decided or expires; on approval the token is stored.</summary>
    public async Task<SignInOutcome> WaitForApprovalAsync(PendingSignIn pending, CancellationToken cancellationToken)
    {
        TimeSpan interval = pending.Interval;
        while (true)
        {
            await clock.Delay(interval, cancellationToken);
            if (clock.UtcNow >= pending.ExpiresAt)
            {
                return SignInOutcome.Expired;
            }

            ApiResult<DeviceTokenResponse> polled = await api.PollTokenAsync(pending.DeviceCode, cancellationToken);
            if (polled.IsSuccess && polled.Value?.AccessToken is { Length: > 0 } token)
            {
                credentials.WriteToken(token);
                return SignInOutcome.SignedIn;
            }

            switch (polled.Status, polled.Error)
            {
                case (HttpStatusCode.UpgradeRequired, _):
                    return SignInOutcome.UpdateRequired;
                case (HttpStatusCode.BadRequest, "access_denied"):
                    return SignInOutcome.Denied;
                case (HttpStatusCode.BadRequest, "expired_token"):
                    return SignInOutcome.Expired;
                case (HttpStatusCode.BadRequest, "slow_down"):
                    interval += SlowDownStep;
                    break;
            }

            // authorization_pending, a network failure or a busy server: keep polling until the code expires.
        }
    }
}
