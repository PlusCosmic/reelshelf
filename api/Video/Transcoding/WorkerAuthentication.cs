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

    /// <summary>The name <see cref="Secret"/> goes by among <see cref="Secrets"/>.</summary>
    public const string DefaultSecretName = "default";

    /// <summary>
    /// The credential transcode workers send as <c>Authorization: Bearer</c>, known as the secret named
    /// <see cref="DefaultSecretName"/>. While no secret here or in <see cref="Secrets"/> is usable (set and at least
    /// <see cref="MinimumSecretLength"/> characters) every worker endpoint refuses every request.
    /// </summary>
    public string Secret { get; set; } = "";

    /// <summary>
    /// More worker credentials by name (<c>Worker:Secrets:aws</c>), so each pool of workers has its own to rotate or
    /// revoke. Each follows the same rules as <see cref="Secret"/>, which wins over an entry named
    /// <see cref="DefaultSecretName"/>.
    /// </summary>
    public Dictionary<string, string?> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Requests per minute each worker address may make, against a worker stuck in a loop.</summary>
    public int RequestsPerMinute { get; set; } = 600;

    /// <summary>Every usable secret, by name.</summary>
    public IReadOnlyList<WorkerSecret> EffectiveSecrets => Evaluate().Usable;

    /// <summary>The secrets that are set but not used, by name, with why, for the startup warning.</summary>
    public IReadOnlyList<(string Name, string Reason)> IgnoredSecrets => Evaluate().Ignored;

    private (List<WorkerSecret> Usable, List<(string Name, string Reason)> Ignored) Evaluate()
    {
        List<WorkerSecret> usable = [];
        List<(string Name, string Reason)> ignored = [];

        void Consider(string name, string? value)
        {
            string trimmed = value?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                return;
            }

            if (trimmed.Length < MinimumSecretLength)
            {
                ignored.Add((name, $"shorter than {MinimumSecretLength} characters"));
                return;
            }

            usable.Add(new WorkerSecret(name, trimmed));
        }

        bool hasDefault = !string.IsNullOrWhiteSpace(Secret);
        Consider(DefaultSecretName, Secret);
        foreach ((string name, string? value) in Secrets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            string trimmedName = name.Trim();
            if (trimmedName.Length == 0 || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (hasDefault && string.Equals(trimmedName, DefaultSecretName, StringComparison.OrdinalIgnoreCase))
            {
                ignored.Add((trimmedName, "Worker:Secret is the secret named default"));
                continue;
            }

            Consider(trimmedName, value);
        }

        return (usable, ignored);
    }
}

/// <summary>A worker credential and the name of the pool of workers it belongs to.</summary>
public sealed record WorkerSecret(string Name, string Value);

/// <summary>Warns at startup about worker secrets that are set but ignored, so a short one doesn't fail silently.</summary>
public sealed class WorkerSecretsCheck(IOptions<WorkerOptions> options, ILogger<WorkerSecretsCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach ((string name, string reason) in options.Value.IgnoredSecrets)
        {
            logger.LogWarning("Ignoring the worker secret named {WorkerSecret}: {Reason}", name, reason);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The "Worker" authentication scheme: one of the worker secrets in the <c>Authorization: Bearer</c> header,
/// compared in constant time against every configured secret, so the time taken doesn't reveal which one matched.
/// The matched secret's name goes on the principal as <see cref="WorkerNameClaim"/>. It is used only by <see cref="Policy"/>, which only the worker endpoints require,
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

    /// <summary>The name of the secret the worker authenticated with (<see cref="WorkerOptions.Secrets"/>).</summary>
    public const string WorkerNameClaim = "urn:reelshelf:worker:name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        IReadOnlyList<WorkerSecret> secrets = workerOptions.CurrentValue.EffectiveSecrets;
        if (secrets.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("The worker API is not configured (Worker:Secret or Worker:Secrets)"));
        }

        string? name = MatchSecret(secrets, header["Bearer ".Length..].Trim());
        if (name is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid worker credential"));
        }

        ClaimsIdentity identity = new([new Claim(WorkerClaim, "true"), new Claim(WorkerNameClaim, name)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <summary>The name of the secret a worker request authenticated with, for the logs.</summary>
    public static string? WorkerName(ClaimsPrincipal user) => user.FindFirst(WorkerNameClaim)?.Value;

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    /// <summary>
    /// The name of the secret <paramref name="provided"/> is, or null. Compares hashes, so each comparison takes the
    /// same time whatever the provided value's length or content, and compares against every secret without stopping
    /// at a match, so the time doesn't depend on which one matched either.
    /// </summary>
    public static string? MatchSecret(IReadOnlyList<WorkerSecret> secrets, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return null;
        }

        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        string? matched = null;
        foreach (WorkerSecret secret in secrets)
        {
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret.Value));
            bool equal = CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
            matched ??= equal ? secret.Name : null;
        }

        return matched;
    }
}
