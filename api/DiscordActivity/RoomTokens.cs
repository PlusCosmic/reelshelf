using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Reelshelf.DiscordActivity;

/// <summary>Who a room token was issued to: one Discord user in one Activity instance, with how Discord names them.</summary>
public sealed record RoomParticipant(string DiscordUserId, string InstanceId, string Name, string? AvatarUrl);

/// <summary>
/// Issues and reads the bearer token the Activity uses for room calls. It is a data-protection payload bound
/// to the Discord user and instance, so it proves nothing about the site session and is accepted only by
/// endpoints that require <see cref="RoomTokenAuthentication.Policy"/>.
/// </summary>
public sealed class RoomTokens(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ITimeLimitedDataProtector _protector = dataProtection
        .CreateProtector("Reelshelf.DiscordActivity.RoomToken.v1")
        .ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Issue(RoomParticipant participant)
    {
        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + Lifetime;
        return (_protector.Protect(JsonSerializer.Serialize(participant), expiresAt), expiresAt);
    }

    /// <summary>Null when the token is malformed, tampered with or expired.</summary>
    public RoomParticipant? Read(string token)
    {
        try
        {
            string json = _protector.Unprotect(token, out DateTimeOffset expiresAt);
            // The protector checks expiry against the wall clock; check again so an injected clock is honoured.
            if (expiresAt <= timeProvider.GetUtcNow())
            {
                return null;
            }

            RoomParticipant? participant = JsonSerializer.Deserialize<RoomParticipant>(json);
            return string.IsNullOrEmpty(participant?.DiscordUserId) || string.IsNullOrEmpty(participant.InstanceId)
                ? null
                : participant;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// The "Activity" authentication scheme: a room token in the <c>Authorization: Bearer</c> header. Its claims
/// deliberately avoid <see cref="ClaimTypes.NameIdentifier"/>, which the rest of the API reads as an account id.
/// </summary>
public sealed class RoomTokenAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    RoomTokens roomTokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Activity";
    public const string Policy = "Activity";

    public const string DiscordUserIdClaim = "urn:reelshelf:activity:discord_user_id";
    public const string InstanceIdClaim = "urn:reelshelf:activity:instance_id";
    public const string NameClaim = "urn:reelshelf:activity:name";
    public const string AvatarUrlClaim = "urn:reelshelf:activity:avatar_url";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        RoomParticipant? participant = roomTokens.Read(header["Bearer ".Length..].Trim());
        if (participant is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired room token"));
        }

        List<Claim> claims =
        [
            new(DiscordUserIdClaim, participant.DiscordUserId),
            new(InstanceIdClaim, participant.InstanceId),
            new(NameClaim, participant.Name)
        ];
        if (participant.AvatarUrl is not null)
        {
            claims.Add(new Claim(AvatarUrlClaim, participant.AvatarUrl));
        }

        ClaimsIdentity identity = new(claims, Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    public static RoomParticipant? ReadParticipant(ClaimsPrincipal principal)
    {
        string? discordUserId = principal.FindFirstValue(DiscordUserIdClaim);
        string? instanceId = principal.FindFirstValue(InstanceIdClaim);
        return string.IsNullOrEmpty(discordUserId) || string.IsNullOrEmpty(instanceId)
            ? null
            : new RoomParticipant(discordUserId, instanceId, principal.FindFirstValue(NameClaim) ?? "",
                principal.FindFirstValue(AvatarUrlClaim));
    }
}
