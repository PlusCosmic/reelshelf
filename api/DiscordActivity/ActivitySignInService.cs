using Reelshelf.Users;

namespace Reelshelf.DiscordActivity;

public enum ActivitySignInOutcome
{
    Ok,
    NotConfigured,
    InvalidCode,
    NotInInstance
}

/// <summary>
/// Who a room participant is. A participant whose Discord login is a linked identity is a room member and
/// carries their account; anyone else is a guest.
/// </summary>
public sealed record ActivityParticipantResponse(
    string DiscordUserId,
    string Name,
    string? AvatarUrl,
    bool IsMember,
    Guid? AccountId,
    string? AccountName);

public sealed record ActivitySessionResponse(
    string AccessToken,
    string RoomToken,
    DateTimeOffset RoomTokenExpiresAt,
    string InstanceId,
    ActivityParticipantResponse Participant);

public sealed record RoomTokenResponse(string RoomToken, DateTimeOffset RoomTokenExpiresAt);

public sealed record ActivitySignInResult(ActivitySignInOutcome Outcome, ActivitySessionResponse? Session = null);

/// <summary>
/// Turns the Embedded App SDK's authorization code into a room token. Discord is asked who is in the instance,
/// so a token is only ever issued to someone actually connected to it.
/// </summary>
public sealed class ActivitySignInService(
    DiscordActivityClient discord,
    IUserIdentityStore identities,
    RoomTokens roomTokens)
{
    public async Task<ActivitySignInResult> SignInAsync(string code, string instanceId, CancellationToken cancellationToken)
    {
        if (!discord.IsConfigured)
        {
            return new ActivitySignInResult(ActivitySignInOutcome.NotConfigured);
        }

        string? accessToken = await discord.ExchangeCodeAsync(code, cancellationToken);
        if (accessToken is null)
        {
            return new ActivitySignInResult(ActivitySignInOutcome.InvalidCode);
        }

        DiscordUser user = await discord.GetCurrentUserAsync(accessToken, cancellationToken);
        IReadOnlyList<string>? connected = await discord.GetInstanceUserIdsAsync(instanceId, cancellationToken);
        if (connected is null || !connected.Contains(user.Id, StringComparer.Ordinal))
        {
            return new ActivitySignInResult(ActivitySignInOutcome.NotInInstance);
        }

        (string roomToken, DateTimeOffset expiresAt) = roomTokens.Issue(
            new RoomParticipant(user.Id, instanceId, user.Name, user.AvatarUrl));
        ActivityParticipantResponse participant = await DescribeAsync(user.Id, user.Name, user.AvatarUrl);

        return new ActivitySignInResult(ActivitySignInOutcome.Ok,
            new ActivitySessionResponse(accessToken, roomToken, expiresAt, instanceId, participant));
    }

    /// <summary>
    /// A fresh room token for someone who already holds one, issued only while Discord still lists them in
    /// the instance. Null when they have left it.
    /// </summary>
    public async Task<RoomTokenResponse?> RefreshAsync(RoomParticipant participant, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? connected = await discord.GetInstanceUserIdsAsync(participant.InstanceId, cancellationToken);
        if (connected is null || !connected.Contains(participant.DiscordUserId, StringComparer.Ordinal))
        {
            return null;
        }

        (string roomToken, DateTimeOffset expiresAt) = roomTokens.Issue(participant);
        return new RoomTokenResponse(roomToken, expiresAt);
    }

    /// <summary>
    /// Resolves membership fresh on every call, so linking Discord to an account mid-room takes effect on the
    /// next request rather than the next token.
    /// </summary>
    public async Task<ActivityParticipantResponse> DescribeAsync(string discordUserId, string name, string? avatarUrl)
    {
        UserStatements.UserIdentityRow? identity = await identities.GetIdentity(AuthProvider.Discord, discordUserId);
        UserStatements.UserRow? account = identity is null ? null : await identities.GetUserById(identity.UserId);

        return account is null
            ? new ActivityParticipantResponse(discordUserId, name, avatarUrl, false, null, null)
            : new ActivityParticipantResponse(discordUserId, name, avatarUrl, true, account.Id,
                account.GlobalName ?? account.Username);
    }
}
