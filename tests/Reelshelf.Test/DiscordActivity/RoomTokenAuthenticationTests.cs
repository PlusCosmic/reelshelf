using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.DiscordActivity;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// Where the Activity scheme looks for a room token: the bearer header anywhere, and the <c>access_token</c>
/// query parameter only on the watch room hub, where SignalR's WebSocket transport cannot send headers.
/// </summary>
public class RoomTokenAuthenticationTests
{
    private static readonly RoomParticipant Alice = new("111", "i-1", "Alice", null);
    private readonly RoomTokens _tokens = new(new EphemeralDataProtectionProvider(), TimeProvider.System);

    [Fact]
    public async Task BearerHeader_Authenticates()
    {
        string token = _tokens.Issue(Alice).Token;

        AuthenticateResult result = await Authenticate("/api/activity/clips", header: token);

        Assert.True(result.Succeeded);
        Assert.Equal(Alice, RoomTokenAuthentication.ReadParticipant(result.Principal!));
    }

    [Fact]
    public async Task QueryToken_AuthenticatesOnlyOnTheHub()
    {
        string token = _tokens.Issue(Alice).Token;

        AuthenticateResult hub = await Authenticate("/api/activity/hub", query: token);
        AuthenticateResult elsewhere = await Authenticate("/api/activity/clips", query: token);

        Assert.True(hub.Succeeded);
        Assert.Equal(Alice, RoomTokenAuthentication.ReadParticipant(hub.Principal!));
        Assert.True(elsewhere.None);
    }

    [Fact]
    public async Task ABadToken_Fails()
    {
        AuthenticateResult result = await Authenticate("/api/activity/hub", query: "junk");

        Assert.False(result.Succeeded);
        Assert.False(result.None);
    }

    private async Task<AuthenticateResult> Authenticate(string path, string? header = null, string? query = null)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        if (header is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {header}";
        }

        if (query is not null)
        {
            context.Request.QueryString = QueryString.Create("access_token", query);
        }

        RoomTokenAuthentication handler = new(
            new StaticOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _tokens);
        await handler.InitializeAsync(
            new AuthenticationScheme(RoomTokenAuthentication.Scheme, null, typeof(RoomTokenAuthentication)),
            context);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
