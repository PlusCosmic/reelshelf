using System.Net;
using System.Text;
using System.Text.Json;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.SignIn;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>Signing in with a device code, and the handler every API request goes through.</summary>
public class SignInTests
{
    [Fact]
    public async Task SigningIn_PollsAtTheInterval_SlowsDownWhenAsked_AndStoresTheToken()
    {
        ScriptedApi server = new();
        server.Answers.Enqueue((HttpStatusCode.OK, new
        {
            device_code = "device-code", user_code = "KDRW-TPLM",
            verification_uri = "https://reelshelf.app/device",
            verification_uri_complete = "https://reelshelf.app/device?code=KDRW-TPLM", interval = 5, expires_in = 600
        }));
        server.Answers.Enqueue((HttpStatusCode.BadRequest, new { error = "authorization_pending" }));
        server.Answers.Enqueue((HttpStatusCode.BadRequest, new { error = "slow_down" }));
        server.Answers.Enqueue((HttpStatusCode.BadRequest, new { error = "authorization_pending" }));
        server.Answers.Enqueue((HttpStatusCode.OK, new { access_token = "rsd_issued", token_type = "Bearer" }));
        (DeviceSignIn signIn, FakeClock clock, MemoryCredentials credentials) = Create(server, token: null);

        SignInResult started = await signIn.StartAsync("Gaming PC", CancellationToken.None);
        Assert.Equal(SignInOutcome.Started, started.Outcome);
        Assert.Equal("KDRW-TPLM", started.Pending!.UserCode);
        Assert.Equal(new Uri("https://reelshelf.app/device?code=KDRW-TPLM"), started.Pending.VerificationUriComplete);
        Assert.Equal("""{"device_name":"Gaming PC"}""", server.Bodies[0]);

        Assert.Equal(SignInOutcome.SignedIn, await signIn.WaitForApprovalAsync(started.Pending, CancellationToken.None));

        Assert.Equal("rsd_issued", credentials.Token);
        Assert.Equal(
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)],
            clock.Delays);
        Assert.All(server.Bodies.Skip(1), body => Assert.Equal("""{"device_code":"device-code"}""", body));
        Assert.All(server.Authorizations, authorization => Assert.Null(authorization));
    }

    [Theory]
    [InlineData("access_denied", SignInOutcome.Denied)]
    [InlineData("expired_token", SignInOutcome.Expired)]
    public async Task ADeniedOrExpiredCode_StopsPolling(string error, SignInOutcome outcome)
    {
        ScriptedApi server = new();
        server.Answers.Enqueue((HttpStatusCode.BadRequest, new { error }));
        (DeviceSignIn signIn, _, MemoryCredentials credentials) = Create(server, token: null);
        PendingSignIn pending = new("device-code", "KDRW-TPLM", new Uri("https://reelshelf.app/device"),
            new Uri("https://reelshelf.app/device?code=KDRW-TPLM"), TimeSpan.FromSeconds(5),
            new FakeClock().UtcNow.AddMinutes(10));

        Assert.Equal(outcome, await signIn.WaitForApprovalAsync(pending, CancellationToken.None));
        Assert.Null(credentials.Token);
    }

    [Fact]
    public async Task ACodeThatOutlivesItsTime_ExpiresWithoutAnAnswer()
    {
        ScriptedApi server = new();
        for (int i = 0; i < 200; i++)
        {
            server.Answers.Enqueue((HttpStatusCode.BadRequest, new { error = "authorization_pending" }));
        }

        (DeviceSignIn signIn, FakeClock clock, _) = Create(server, token: null);
        PendingSignIn pending = new("device-code", "KDRW-TPLM", new Uri("https://reelshelf.app/device"),
            new Uri("https://reelshelf.app/device?code=KDRW-TPLM"), TimeSpan.FromSeconds(5), clock.UtcNow.AddMinutes(10));

        Assert.Equal(SignInOutcome.Expired, await signIn.WaitForApprovalAsync(pending, CancellationToken.None));
        Assert.Equal(119, server.Bodies.Count);
    }

    [Fact]
    public async Task TheHandler_AddsTheTokenAndClient_AndRaisesSignedOutAndUpdateRequired()
    {
        ScriptedApi server = new();
        server.Answers.Enqueue((HttpStatusCode.OK, new { }));
        server.Answers.Enqueue((HttpStatusCode.Unauthorized, new { }));
        server.Answers.Enqueue(((HttpStatusCode)426, new { error = "client_too_old", minimum_version = "1.2.0" }));
        ReelshelfClientHandler handler = new(() => "rsd_secret", "1.0.3", server);
        int signedOut = 0;
        int updateRequired = 0;
        handler.SignedOut += (_, _) => signedOut++;
        handler.UpdateRequired += (_, _) => updateRequired++;
        using ReelshelfApi api = new(FakeReelshelf.ApiBase, handler);
        UploaderApi uploader = new(api);

        await uploader.GetPlanAsync(CancellationToken.None);
        Assert.Equal("Bearer rsd_secret", server.Authorizations[0]);
        Assert.Equal("uploader/1.0.3", server.Clients[0]);
        Assert.Equal((0, 0), (signedOut, updateRequired));

        var refused = await uploader.GetPlanAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal((1, 0), (signedOut, updateRequired));

        var tooOld = await uploader.GetPlanAsync(CancellationToken.None);
        Assert.Equal((HttpStatusCode)426, tooOld.Status);
        Assert.Equal("client_too_old", tooOld.Error);
        Assert.Equal((1, 1), (signedOut, updateRequired));
    }

    [Fact]
    public async Task WithoutAToken_A401IsntASignOut()
    {
        ScriptedApi server = new();
        server.Answers.Enqueue((HttpStatusCode.Unauthorized, new { }));
        ReelshelfClientHandler handler = new(() => null, "1.0.3", server);
        int signedOut = 0;
        handler.SignedOut += (_, _) => signedOut++;
        using ReelshelfApi api = new(FakeReelshelf.ApiBase, handler);

        await new UploaderApi(api).GetPlanAsync(CancellationToken.None);

        Assert.Equal(0, signedOut);
        Assert.Null(server.Authorizations[0]);
    }

    private static (DeviceSignIn SignIn, FakeClock Clock, MemoryCredentials Credentials) Create(ScriptedApi server,
        string? token)
    {
        MemoryCredentials credentials = new(token);
        ReelshelfClientHandler handler = new(() => credentials.ReadToken(), "1.0.3", server);
        ReelshelfApi api = new(FakeReelshelf.ApiBase, handler);
        FakeClock clock = new();
        return (new DeviceSignIn(new UploaderApi(api), credentials, clock), clock, credentials);
    }

    /// <summary>Answers requests in the order given, recording each.</summary>
    private sealed class ScriptedApi : HttpMessageHandler
    {
        public Queue<(HttpStatusCode Status, object Body)> Answers { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<string?> Authorizations { get; } = [];
        public List<string?> Clients { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Authorizations.Add(request.Headers.Authorization?.ToString());
            Clients.Add(request.Headers.TryGetValues(ReelshelfClientHandler.ClientHeader, out var values)
                ? values.Single()
                : null);
            (HttpStatusCode status, object body) = Answers.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
        }
    }
}
