namespace Reelshelf.Users;

/// <summary>
/// Decides what an external sign-in means for accounts: which account it belongs to, whether one must be
/// created, and how identities are attached to or detached from an existing account.
///
/// The oldest linked identity is the account's <b>primary</b> identity: the account's username, display name
/// and avatar follow it. Signing in with a secondary identity refreshes only that identity's own profile.
/// The account email is the user's own choice and is never overwritten by a provider.
/// </summary>
public sealed class AccountLinkingService(IUserIdentityStore store)
{
    public async Task<SignInOutcome> SignIn(ExternalIdentity identity)
    {
        // Two passes cover the races around a first sign-in: the identity being unlinked between lookup and
        // lock, or a concurrent callback for the same identity creating the account first.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            UserStatements.UserIdentityRow? existing = await store.GetIdentity(identity.Provider, identity.ProviderUserId);
            if (existing is not null)
            {
                SignInOutcome? refreshed = await RefreshExisting(existing, identity);
                if (refreshed is not null)
                {
                    return refreshed;
                }

                // Unlinked between the lookup and the lock: treat it as a first sign-in.
            }

            UserStatements.UserRow? created = await store.CreateUserWithIdentity(identity);
            if (created is not null)
            {
                return new SignInOutcome(created, Created: true);
            }

            // Lost the race to another first sign-in for this identity; it exists now, so look it up again.
        }

        throw new InvalidOperationException($"Could not resolve an account for {identity.Provider} identity {identity.ProviderUserId}");
    }

    /// <summary>
    /// Refreshes a known identity (and the account profile when it is primary) under the account lock, so an
    /// unlink racing this sign-in cannot have its promoted successor overwritten. Returns null when the identity
    /// no longer belongs to the account; the lock is released before the caller creates anything.
    /// </summary>
    private async Task<SignInOutcome?> RefreshExisting(UserStatements.UserIdentityRow existing, ExternalIdentity identity)
    {
        await using IAccountScope scope = await store.LockAccount(existing.UserId);

        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(existing.UserId);
        UserStatements.UserIdentityRow? current = identities.FirstOrDefault(i => i.Id == existing.Id);
        if (current is null)
        {
            return null;
        }

        await store.UpdateIdentityProfile(current.Id, identity);
        if (identities[0].Id == current.Id)
        {
            await store.UpdateUserProfile(existing.UserId, identity.Username, identity.DisplayName, identity.AvatarUrl);
        }

        UserStatements.UserRow user = await store.GetUserById(existing.UserId)
                                      ?? throw new InvalidOperationException("Identity points at a missing account");
        await scope.CommitAsync();
        return new SignInOutcome(user, Created: false);
    }

    public async Task<LinkOutcome> Link(Guid userId, ExternalIdentity identity)
    {
        // The account lock serialises links and unlinks for this account; the database's unique constraints
        // still catch a race between two different accounts claiming the same provider identity.
        await using IAccountScope scope = await store.LockAccount(userId);

        UserStatements.UserIdentityRow? existing = await store.GetIdentity(identity.Provider, identity.ProviderUserId);
        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                return LinkOutcome.LinkedToAnotherAccount;
            }

            await store.UpdateIdentityProfile(existing.Id, identity);
            await scope.CommitAsync();
            return LinkOutcome.AlreadyLinked;
        }

        List<UserStatements.UserIdentityRow> identities = await store.GetIdentitiesForUser(userId);
        if (identities.Any(i => i.Provider == identity.Provider))
        {
            // One identity per provider keeps "unlink Twitch" unambiguous.
            return LinkOutcome.ProviderAlreadyLinked;
        }

        UserStatements.UserIdentityRow? linked = await store.LinkIdentity(userId, identity);
        if (linked is null)
        {
            return LinkOutcome.LinkedToAnotherAccount;
        }

        await scope.CommitAsync();
        return LinkOutcome.Linked;
    }

    public async Task<UnlinkOutcome> Unlink(Guid userId, string provider)
    {
        // Count, delete and successor promotion happen under one lock and transaction, so two concurrent
        // unlinks cannot both pass the last-identity check, and a failure after the delete rolls it back.
        await using IAccountScope scope = await store.LockAccount(userId);

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

        await scope.CommitAsync();
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
                identity.Email,
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
    /// <summary>Null when the identity already exists; nothing is left behind in that case.</summary>
    Task<UserStatements.UserRow?> CreateUserWithIdentity(ExternalIdentity identity);
    /// <summary>Null when the identity already exists for some account.</summary>
    Task<UserStatements.UserIdentityRow?> LinkIdentity(Guid userId, ExternalIdentity identity);

    /// <summary>Serialises identity changes for one account; dispose without committing to roll back.</summary>
    Task<IAccountScope> LockAccount(Guid userId);
    Task UpdateIdentityProfile(Guid identityId, ExternalIdentity identity);
    Task UpdateUserProfile(Guid userId, string username, string? globalName, string? avatarUrl);
    Task DeleteIdentity(Guid identityId);
}

public interface IAccountScope : IAsyncDisposable
{
    Task CommitAsync();
}
