using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>A secret shorter than this counts as unset, so a placeholder never opens the worker API.</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>
    /// The credential transcode workers send as <c>Authorization: Bearer</c>. While it is unset (or shorter than
    /// <see cref="MinimumSecretLength"/>) every worker endpoint refuses every request.
    /// </summary>
    public string Secret { get; set; } = "";

    /// <summary>Requests per minute each worker address may make, against a worker stuck in a loop.</summary>
    public int RequestsPerMinute { get; set; } = 600;

    public string? EffectiveSecret
    {
        get
        {
            string trimmed = Secret.Trim();
            return trimmed.Length >= MinimumSecretLength ? trimmed : null;
        }
    }
}

/// <summary>
/// The "Worker" authentication scheme: the shared worker secret in the <c>Authorization: Bearer</c> header,
/// compared in constant time. It is used only by <see cref="Policy"/>, which only the worker endpoints require,
/// so the secret grants nothing else and the site cookie never satisfies the policy. Its principal deliberately
/// has no <see cref="ClaimTypes.NameIdentifier"/>, which the rest of the API reads as an account id.
/// </summary>
public sealed class WorkerAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<WorkerOptions> workerOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Worker";
    public const string Policy = "Worker";
    public const string WorkerClaim = "urn:reelshelf:worker";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? secret = workerOptions.CurrentValue.EffectiveSecret;
        if (secret is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("The worker API is not configured (Worker:Secret)"));
        }

        if (!SecretMatches(secret, header["Bearer ".Length..].Trim()))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid worker credential"));
        }

        ClaimsIdentity identity = new([new Claim(WorkerClaim, "true")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares hashes of the two, so the comparison takes the same time whatever the provided value's length
    /// or content.
    /// </summary>
    public static bool SecretMatches(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
