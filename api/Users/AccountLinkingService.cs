namespace Reelshelf.Users;

/// <summary>
/// Decides what an external sign-in means for accounts: which account it belongs to, whether one must be
/// created, and how identities are attached to or detached from an existing account.
///
/// The oldest linked identity is the account's <b>primary</b> identity: the account's username, display name
/// and avatar follow it. Signing in with a secondary identity refreshes only that identity's own profile.
/// </summary>
public sealed class AccountLinkingService(IUserIdentityStore store)
{
    public async Task<SignInOutcome> SignIn(ExternalIdentity identity)
    {
        UserStatements.UserIdentityRow? existing = await store.GetIdentity(identity.Provider, identity.ProviderUserId);
        if (existing is null)
        {
            UserStatements.UserRow created = await store.CreateUserWithIdentity(identity);
            return new SignInOutcome(created, Created: true);
        }

        await store.UpdateIdentityProfile(existing.Id, identity);

        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(existing.UserId);
        if (identities.Count == 0 || identities[0].Id == existing.Id)
        {
            await store.UpdateUserProfile(existing.UserId, identity.Username, identity.DisplayName, identity.AvatarUrl);
        }

        UserStatements.UserRow user = await store.GetUserById(existing.UserId)
                                      ?? throw new InvalidOperationException("Identity points at a missing account");
        return new SignInOutcome(user, Created: false);
    }

    public async Task<LinkOutcome> Link(Guid userId, ExternalIdentity identity)
    {
        UserStatements.UserIdentityRow? existing = await store.GetIdentity(identity.Provider, identity.ProviderUserId);
        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                return LinkOutcome.LinkedToAnotherAccount;
            }

            await store.UpdateIdentityProfile(existing.Id, identity);
            return LinkOutcome.AlreadyLinked;
        }

        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(userId);
        if (identities.Any(i => i.Provider == identity.Provider))
        {
            // One identity per provider keeps "unlink Twitch" unambiguous.
            return LinkOutcome.ProviderAlreadyLinked;
        }

        await store.LinkIdentity(userId, identity);
        return LinkOutcome.Linked;
    }

    public async Task<UnlinkOutcome> Unlink(Guid userId, string provider)
    {
        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(userId);
        UserStatements.UserIdentityRow? target = identities.FirstOrDefault(i => i.Provider == provider);
        if (target is null)
        {
            return UnlinkOutcome.NotLinked;
        }

        if (identities.Count == 1)
        {
            return UnlinkOutcome.LastIdentity;
        }

        await store.DeleteIdentity(target.Id);

        if (identities[0].Id == target.Id)
        {
            // The next-oldest identity becomes primary and the account profile follows it.
            UserStatements.UserIdentityRow successor = identities[1];
            await store.UpdateUserProfile(userId, successor.Username, successor.DisplayName, successor.AvatarUrl);
        }

        return UnlinkOutcome.Unlinked;
    }

    public async Task<List<LinkedIdentity>> GetLinkedIdentities(Guid userId)
    {
        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(userId);
        return identities
            .Select((identity, index) => new LinkedIdentity(
                identity.Provider,
                identity.ProviderUserId,
                identity.Username,
                identity.DisplayName,
                identity.AvatarUrl,
                identity.LinkedAt,
                IsPrimary: index == 0))
            .ToList();
    }
}

public sealed record SignInOutcome(UserStatements.UserRow User, bool Created);

public enum LinkOutcome
{
    Linked,
    AlreadyLinked,
    ProviderAlreadyLinked,
    LinkedToAnotherAccount
}

public enum UnlinkOutcome
{
    Unlinked,
    NotLinked,
    LastIdentity
}

public interface IUserIdentityStore
{
    Task<UserStatements.UserRow?> GetUserById(Guid id);
    Task<UserStatements.UserIdentityRow?> GetIdentity(string provider, string providerUserId);
    Task<List<UserStatements.UserIdentityRow>> GetIdentitiesForUser(Guid userId);
    Task<UserStatements.UserRow> CreateUserWithIdentity(ExternalIdentity identity);
    Task<UserStatements.UserIdentityRow> LinkIdentity(Guid userId, ExternalIdentity identity);
    Task UpdateIdentityProfile(Guid identityId, ExternalIdentity identity);
    Task UpdateUserProfile(Guid userId, string username, string? globalName, string? avatarUrl);
    Task DeleteIdentity(Guid identityId);
}
