using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Reelshelf.DiscordActivity;
using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// Runs Activity sign-in against canned Discord replies: a room token is only issued to someone Discord lists
/// in the instance, membership follows the linked Discord identity, and the token is bound to user and instance.
/// </summary>
public class ActivitySignInTests
{
    private const string InstanceId = "i-1234-gc-5678";
    private const string UserJson = """{ "id": "111", "username": "alice", "global_name": "Alice", "avatar": "abc" }""";

    [Fact]
    public async Task Member_GetsARoomTokenBoundToThemAndTheInstance()
    {
        Guid accountId = Guid.NewGuid();
        FakeStore store = new();
        store.Add("111", accountId, "alice", "Alice R");
        FakeDiscord discord = new(instanceUsers: """["222", "111"]""");
        Clock clock = new();
        RoomTokens tokens = new(new EphemeralDataProtectionProvider(), clock);

        ActivitySignInResult result = await CreateService(discord, store, tokens).SignInAsync("the-code", InstanceId, CancellationToken.None);

        Assert.Equal(ActivitySignInOutcome.Ok, result.Outcome);
        ActivitySessionResponse session = result.Session!;
        Assert.Equal("discord-access", session.AccessToken);
        Assert.Equal(clock.GetUtcNow() + RoomTokens.Lifetime, session.RoomTokenExpiresAt);
        Assert.Equal(new ActivityParticipantResponse("111", "Alice", "https://cdn.discordapp.com/avatars/111/abc", true, accountId, "Alice R"),
            session.Participant);
        Assert.Equal(new RoomParticipant("111", InstanceId, "Alice", "https://cdn.discordapp.com/avatars/111/abc"),
            tokens.Read(session.RoomToken));

        Assert.Contains("code=the-code", discord.TokenRequestBody);
        Assert.Contains("grant_type=authorization_code", discord.TokenRequestBody);
        Assert.Equal("Bot bot-token", discord.InstanceAuthorization);
        Assert.EndsWith($"/applications/client-id/activity-instances/{InstanceId}", discord.InstanceRequestPath);
    }

    [Fact]
    public async Task SomeoneWithoutALinkedDiscordIdentity_IsAGuest()
    {
        ActivitySignInResult result = await CreateService(new FakeDiscord(instanceUsers: """["111"]"""), new FakeStore())
            .SignInAsync("the-code", InstanceId, CancellationToken.None);

        Assert.Equal(ActivitySignInOutcome.Ok, result.Outcome);
        Assert.False(result.Session!.Participant.IsMember);
        Assert.Null(result.Session.Participant.AccountId);
    }

    [Fact]
    public async Task SomeoneNotConnectedToTheInstance_GetsNoToken()
    {
        ActivitySignInResult notListed = await CreateService(new FakeDiscord(instanceUsers: """["222"]"""), new FakeStore())
            .SignInAsync("the-code", InstanceId, CancellationToken.None);
        ActivitySignInResult notRunning = await CreateService(new FakeDiscord(instanceUsers: null), new FakeStore())
            .SignInAsync("the-code", InstanceId, CancellationToken.None);

        Assert.Equal(ActivitySignInOutcome.NotInInstance, notListed.Outcome);
        Assert.Null(notListed.Session);
        Assert.Equal(ActivitySignInOutcome.NotInInstance, notRunning.Outcome);
    }

    [Fact]
    public async Task ARefusedCode_IsInvalid()
    {
        ActivitySignInResult result = await CreateService(new FakeDiscord(instanceUsers: """["111"]""", refuseCode: true), new FakeStore())
            .SignInAsync("stale", InstanceId, CancellationToken.None);

        Assert.Equal(ActivitySignInOutcome.InvalidCode, result.Outcome);
    }

    [Fact]
    public async Task WithoutABotToken_TheActivityIsOff_AndDiscordIsNotCalled()
    {
        FakeDiscord discord = new(instanceUsers: """["111"]""");

        ActivitySignInResult result = await CreateService(discord, new FakeStore(), botToken: "")
            .SignInAsync("the-code", InstanceId, CancellationToken.None);

        Assert.Equal(ActivitySignInOutcome.NotConfigured, result.Outcome);
        Assert.Null(discord.TokenRequestBody);
    }

    [Fact]
    public void RoomTokens_RejectExpiredAndTamperedTokens()
    {
        Clock clock = new();
        RoomTokens tokens = new(new EphemeralDataProtectionProvider(), clock);
        (string token, _) = tokens.Issue(new RoomParticipant("111", InstanceId, "Alice", null));

        Assert.NotNull(tokens.Read(token));
        Assert.Null(tokens.Read(token[..^4] + "AAAA"));
        Assert.Null(tokens.Read("not a token"));

        clock.Advance(RoomTokens.Lifetime);
        Assert.Null(tokens.Read(token));
    }

    [Fact]
    public void RoomTokenClaims_NeverCarryANameIdentifier()
    {
        // The rest of the API reads NameIdentifier as an account id; an Activity principal must not look like one.
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim(RoomTokenAuthentication.DiscordUserIdClaim, "111"),
            new Claim(RoomTokenAuthentication.InstanceIdClaim, InstanceId),
            new Claim(RoomTokenAuthentication.NameClaim, "Alice")
        ], RoomTokenAuthentication.Scheme));

        Assert.Null(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(new RoomParticipant("111", InstanceId, "Alice", null), RoomTokenAuthentication.ReadParticipant(principal));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    [InlineData(null, false)]
    public void OnlyEncodedClipsArePlayable(int? videoStatus, bool playable)
    {
        Assert.Equal(playable, ActivityClipService.IsPlayable(videoStatus));
    }

    private static ActivitySignInService CreateService(FakeDiscord discord, FakeStore store, RoomTokens? tokens = null, string botToken = "bot-token")
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DiscordClientId"] = "client-id",
                ["DiscordClientSecret"] = "client-secret",
                [DiscordActivityOptions.BotTokenKey] = botToken
            })
            .Build();

        return new ActivitySignInService(
            new DiscordActivityClient(new SingleClientFactory(discord), configuration),
            store,
            tokens ?? new RoomTokens(new EphemeralDataProtectionProvider(), new Clock()));
    }

    private sealed class FakeDiscord(string? instanceUsers, bool refuseCode = false) : HttpMessageHandler
    {
        public string? TokenRequestBody { get; private set; }
        public string? InstanceAuthorization { get; private set; }
        public string? InstanceRequestPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth2/token"))
            {
                TokenRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return refuseCode
                    ? Reply(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""")
                    : Reply(HttpStatusCode.OK, """{ "access_token": "discord-access", "token_type": "Bearer" }""");
            }

            if (path.EndsWith("/users/@me"))
            {
                return Reply(HttpStatusCode.OK, UserJson);
            }

            InstanceAuthorization = request.Headers.Authorization?.ToString();
            InstanceRequestPath = path;
            return instanceUsers is null
                ? Reply(HttpStatusCode.NotFound, """{ "message": "Unknown Activity Instance" }""")
                : Reply(HttpStatusCode.OK, $$"""{ "instance_id": "{{InstanceId}}", "users": {{instanceUsers}} }""");
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeStore : IUserIdentityStore
    {
        private readonly Dictionary<string, UserStatements.UserIdentityRow> _identities = [];
        private readonly Dictionary<Guid, UserStatements.UserRow> _users = [];

        public void Add(string discordId, Guid accountId, string username, string? globalName)
        {
            _identities[discordId] = new UserStatements.UserIdentityRow
            {
                Id = Guid.NewGuid(), UserId = accountId, Provider = AuthProvider.Discord, ProviderUserId = discordId, Username = username
            };
            _users[accountId] = new UserStatements.UserRow { Id = accountId, Username = username, GlobalName = globalName };
        }

        public Task<UserStatements.UserRow?> GetUserById(Guid id) => Task.FromResult(_users.GetValueOrDefault(id));

        public Task<UserStatements.UserIdentityRow?> GetIdentity(string provider, string providerUserId) =>
            Task.FromResult(provider == AuthProvider.Discord ? _identities.GetValueOrDefault(providerUserId) : null);

        public Task<List<UserStatements.UserIdentityRow>> GetIdentitiesForUser(Guid userId) => throw new NotSupportedException();
        public Task<UserStatements.UserRow?> CreateUserWithIdentity(ExternalIdentity identity) => throw new NotSupportedException();
        public Task<UserStatements.UserIdentityRow?> LinkIdentity(Guid userId, ExternalIdentity identity) => throw new NotSupportedException();
        public Task<IAccountScope> LockAccount(Guid userId) => throw new NotSupportedException();
        public Task UpdateIdentityProfile(Guid identityId, ExternalIdentity identity) => throw new NotSupportedException();
        public Task UpdateUserProfile(Guid userId, string username, string? globalName, string? avatarUrl) => throw new NotSupportedException();
        public Task DeleteIdentity(Guid identityId) => throw new NotSupportedException();
    }
}
