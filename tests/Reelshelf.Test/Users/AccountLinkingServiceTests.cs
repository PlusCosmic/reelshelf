using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Users;

public class AccountLinkingServiceTests
{
    private static readonly ExternalIdentity DiscordHarry =
        new(AuthProvider.Discord, "1001", "harry", "Harry", "https://cdn.discordapp.com/avatars/1001/abc");

    private static readonly ExternalIdentity TwitchHarry =
        new(AuthProvider.Twitch, "2002", "harry_tv", "HarryTV", "https://static-cdn.jtvnw.net/harry.png");

    [Fact]
    public async Task SignIn_CreatesAccountForUnknownIdentity()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);

        SignInOutcome outcome = await service.SignIn(DiscordHarry);

        Assert.True(outcome.Created);
        Assert.Equal("harry", outcome.User.Username);
        Assert.Equal("Harry", outcome.User.GlobalName);
        Assert.Equal(DiscordHarry.AvatarUrl, outcome.User.AvatarUrl);
        UserStatements.UserIdentityRow identity = Assert.Single(store.Identities.Values);
        Assert.Equal(outcome.User.Id, identity.UserId);
    }

    [Fact]
    public async Task SignIn_WithPrimaryIdentity_RefreshesAccountProfile()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome first = await service.SignIn(DiscordHarry);

        ExternalIdentity renamed = DiscordHarry with { Username = "harry2", DisplayName = "Harry II", AvatarUrl = null };
        SignInOutcome second = await service.SignIn(renamed);

        Assert.False(second.Created);
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal("harry2", second.User.Username);
        Assert.Equal("Harry II", second.User.GlobalName);
        Assert.Null(second.User.AvatarUrl);
    }

    [Fact]
    public async Task SignIn_WithSecondaryIdentity_LeavesAccountProfileAlone()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome created = await service.SignIn(DiscordHarry);
        Assert.Equal(LinkOutcome.Linked, await service.Link(created.User.Id, TwitchHarry));

        SignInOutcome viaTwitch = await service.SignIn(TwitchHarry with { Username = "harry_live" });

        Assert.Equal(created.User.Id, viaTwitch.User.Id);
        Assert.Equal("harry", viaTwitch.User.Username);
        Assert.Equal("harry_live", store.Identities.Values.Single(i => i.Provider == AuthProvider.Twitch).Username);
    }

    [Fact]
    public async Task Link_RefusesIdentityOwnedByAnotherAccount()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome harry = await service.SignIn(DiscordHarry);
        SignInOutcome other = await service.SignIn(TwitchHarry);

        LinkOutcome outcome = await service.Link(harry.User.Id, TwitchHarry);

        Assert.Equal(LinkOutcome.LinkedToAnotherAccount, outcome);
        Assert.Equal(other.User.Id, store.Identities.Values.Single(i => i.Provider == AuthProvider.Twitch).UserId);
    }

    [Fact]
    public async Task Link_SameIdentityAgain_IsIdempotent()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome harry = await service.SignIn(DiscordHarry);
        await service.Link(harry.User.Id, TwitchHarry);

        LinkOutcome outcome = await service.Link(harry.User.Id, TwitchHarry);

        Assert.Equal(LinkOutcome.AlreadyLinked, outcome);
        Assert.Equal(2, store.Identities.Count);
    }

    [Fact]
    public async Task Link_SecondIdentityForSameProvider_IsRefused()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome harry = await service.SignIn(DiscordHarry);

        LinkOutcome outcome = await service.Link(harry.User.Id, DiscordHarry with { ProviderUserId = "1002" });

        Assert.Equal(LinkOutcome.ProviderAlreadyLinked, outcome);
        Assert.Single(store.Identities);
    }

    [Fact]
    public async Task Unlink_RefusesLastIdentity()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome harry = await service.SignIn(DiscordHarry);

        Assert.Equal(UnlinkOutcome.LastIdentity, await service.Unlink(harry.User.Id, AuthProvider.Discord));
        Assert.Equal(UnlinkOutcome.NotLinked, await service.Unlink(harry.User.Id, AuthProvider.Twitch));
        Assert.Single(store.Identities);
    }

    [Fact]
    public async Task Unlink_PrimaryIdentity_PromotesNextAndFollowsItsProfile()
    {
        FakeStore store = new();
        AccountLinkingService service = new(store);
        SignInOutcome harry = await service.SignIn(DiscordHarry);
        await service.Link(harry.User.Id, TwitchHarry);

        UnlinkOutcome outcome = await service.Unlink(harry.User.Id, AuthProvider.Discord);

        Assert.Equal(UnlinkOutcome.Unlinked, outcome);
        UserStatements.UserRow user = store.Users[harry.User.Id];
        Assert.Equal("harry_tv", user.Username);
        Assert.Equal("HarryTV", user.GlobalName);
        List<LinkedIdentity> remaining = await service.GetLinkedIdentities(harry.User.Id);
        LinkedIdentity twitch = Assert.Single(remaining);
        Assert.True(twitch.IsPrimary);
    }

    private sealed class FakeStore : IUserIdentityStore
    {
        private DateTimeOffset _clock = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        public Dictionary<Guid, UserStatements.UserRow> Users { get; } = [];
        public Dictionary<Guid, UserStatements.UserIdentityRow> Identities { get; } = [];

        public Task<UserStatements.UserRow?> GetUserById(Guid id)
        {
            return Task.FromResult(Users.GetValueOrDefault(id));
        }

        public Task<UserStatements.UserIdentityRow?> GetIdentity(string provider, string providerUserId)
        {
            return Task.FromResult(Identities.Values.FirstOrDefault(i =>
                i.Provider == provider && i.ProviderUserId == providerUserId));
        }

        public Task<List<UserStatements.UserIdentityRow>> GetIdentitiesForUser(Guid userId)
        {
            return Task.FromResult(Identities.Values
                .Where(i => i.UserId == userId)
                .OrderBy(i => i.LinkedAt)
                .ThenBy(i => i.Id)
                .ToList());
        }

        public Task<UserStatements.UserRow> CreateUserWithIdentity(ExternalIdentity identity)
        {
            UserStatements.UserRow user = new()
            {
                Id = Guid.NewGuid(),
                Username = identity.Username,
                GlobalName = identity.DisplayName,
                AvatarUrl = identity.AvatarUrl
            };
            Users[user.Id] = user;
            Insert(user.Id, identity);
            return Task.FromResult(user);
        }

        public Task<UserStatements.UserIdentityRow> LinkIdentity(Guid userId, ExternalIdentity identity)
        {
            return Task.FromResult(Insert(userId, identity));
        }

        public Task UpdateIdentityProfile(Guid identityId, ExternalIdentity identity)
        {
            UserStatements.UserIdentityRow row = Identities[identityId];
            row.Username = identity.Username;
            row.DisplayName = identity.DisplayName;
            row.AvatarUrl = identity.AvatarUrl;
            return Task.CompletedTask;
        }

        public Task UpdateUserProfile(Guid userId, string username, string? globalName, string? avatarUrl)
        {
            UserStatements.UserRow user = Users[userId];
            user.Username = username;
            user.GlobalName = globalName;
            user.AvatarUrl = avatarUrl;
            return Task.CompletedTask;
        }

        public Task DeleteIdentity(Guid identityId)
        {
            Identities.Remove(identityId);
            return Task.CompletedTask;
        }

        private UserStatements.UserIdentityRow Insert(Guid userId, ExternalIdentity identity)
        {
            if (Identities.Values.Any(i => i.Provider == identity.Provider && i.ProviderUserId == identity.ProviderUserId))
            {
                throw new InvalidOperationException("duplicate identity");
            }

            _clock = _clock.AddMinutes(1);
            UserStatements.UserIdentityRow row = new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = identity.Provider,
                ProviderUserId = identity.ProviderUserId,
                Username = identity.Username,
                DisplayName = identity.DisplayName,
                AvatarUrl = identity.AvatarUrl,
                LinkedAt = _clock
            };
            Identities[row.Id] = row;
            return row;
        }
    }
}
