using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The worker API's credential: the scheme on its own, and the endpoints as mapped, served by Kestrel on a
/// loopback port with a stand-in site scheme that signs every request in, as a site cookie would.
/// </summary>
public class WorkerAuthenticationTests
{
    private const string Secret = "worker-secret-0123456789abcdef-0123456789";
    private const string AwsSecret = "aws-overflow-secret-0123456789abcdef-01234";
    private const string HomeSecret = "home-server-secret-0123456789abcdef-012345";

    private static readonly string[] Endpoints =
    [
        "/api/worker/claim",
        $"/api/worker/{Guid.NewGuid()}/progress",
        $"/api/worker/{Guid.NewGuid()}/complete",
        $"/api/worker/{Guid.NewGuid()}/fail",
        $"/api/worker/{Guid.NewGuid()}/release"
    ];

    [Fact]
    public async Task TheSecret_Authenticates_AsAWorkerWithNoAccount()
    {
        AuthenticateResult result = await Authenticate(Secret, $"Bearer {Secret}");

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.HasClaim(WorkerAuthentication.WorkerClaim, "true"));
        Assert.Null(result.Principal.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Equal(WorkerOptions.DefaultSecretName, WorkerAuthentication.WorkerName(result.Principal));
    }

    [Theory]
    [InlineData(Secret, "default")]
    [InlineData(AwsSecret, "aws")]
    [InlineData(HomeSecret, "home")]
    public async Task EachSecret_Authenticates_WithItsNameOnThePrincipal(string sent, string name)
    {
        AuthenticateResult result = await Authenticate(Named(Secret), $"Bearer {sent}");

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.HasClaim(WorkerAuthentication.WorkerClaim, "true"));
        Assert.Equal(name, WorkerAuthentication.WorkerName(result.Principal));
        Assert.Null(result.Principal.FindFirst(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task NamedSecrets_WorkWithoutTheDefault()
    {
        AuthenticateResult result = await Authenticate(Named(secret: ""), $"Bearer {AwsSecret}");

        Assert.True(result.Succeeded);
        Assert.Equal("aws", WorkerAuthentication.WorkerName(result.Principal!));
        Assert.False((await Authenticate(Named(secret: ""), $"Bearer {Secret}")).Succeeded);
    }

    [Fact]
    public async Task AShortNamedSecret_IsIgnored_AndTheOthersStillWork()
    {
        WorkerOptions options = Named(Secret);
        options.Secrets["short"] = "short-secret";

        Assert.False((await Authenticate(options, "Bearer short-secret")).Succeeded);
        Assert.True((await Authenticate(options, $"Bearer {AwsSecret}")).Succeeded);
        Assert.DoesNotContain(options.EffectiveSecrets, secret => secret.Name == "short");
        Assert.Contains(options.IgnoredSecrets, ignored => ignored.Name == "short");
    }

    [Fact]
    public void TheSecretSetting_WinsOverANamedDefault()
    {
        WorkerOptions options = Named(Secret);
        options.Secrets["default"] = AwsSecret + "-other";

        Assert.Equal([Secret], options.EffectiveSecrets.Where(s => s.Name == "default").Select(s => s.Value));
        Assert.Contains(options.IgnoredSecrets, ignored => ignored.Name == "default");
    }

    [Fact]
    public void UnsetSecrets_AreNotWarnedAbout()
    {
        WorkerOptions options = new() { Secret = "", Secrets = { ["aws"] = "", ["home"] = null } };

        Assert.Empty(options.EffectiveSecrets);
        Assert.Empty(options.IgnoredSecrets);
    }

    [Fact]
    public void MatchSecret_ComparesAgainstEverySecret()
    {
        WorkerSecret[] secrets = [new("a", AwsSecret), new("b", HomeSecret), new("c", Secret)];

        Assert.Equal("c", WorkerAuthentication.MatchSecret(secrets, Secret));
        Assert.Equal("a", WorkerAuthentication.MatchSecret(secrets, AwsSecret));
        Assert.Null(WorkerAuthentication.MatchSecret(secrets, "nope"));
        Assert.Null(WorkerAuthentication.MatchSecret(secrets, ""));
        Assert.Null(WorkerAuthentication.MatchSecret([], Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic d29ya2Vy")]
    public async Task NoBearerHeader_IsNoResult(string? header)
    {
        Assert.True((await Authenticate(Secret, header)).None);
    }

    [Theory]
    [InlineData("Bearer wrong")]
    [InlineData("Bearer ")]
    [InlineData($"Bearer {Secret}x")]
    public async Task AWrongSecret_Fails(string header)
    {
        AuthenticateResult result = await Authenticate(Secret, header);

        Assert.False(result.Succeeded);
        Assert.False(result.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-secret")]
    public async Task WithoutAUsableSecret_EverythingFails(string configured)
    {
        // Even a request that sends exactly the configured value.
        AuthenticateResult result = await Authenticate(configured, $"Bearer {configured}");

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short-secret")]
    public async Task WithOnlyUnusableNamedSecrets_EverythingFails(string configured)
    {
        WorkerOptions options = new() { Secret = configured, Secrets = { ["aws"] = configured, ["home"] = "  " } };

        foreach (string header in new[] { $"Bearer {configured}", $"Bearer {Secret}", "Bearer " })
        {
            Assert.False((await Authenticate(options, header)).Succeeded);
        }
    }

    [Fact]
    public async Task Endpoints_RefuseMissingAndWrongSecrets_AndTheSiteSession()
    {
        await using WorkerApi api = await WorkerApi.StartAsync(Secret);

        foreach (string endpoint in Endpoints)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: null));
            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: "not-the-secret"));
        }
    }

    [Fact]
    public async Task Endpoints_RefuseEverything_WhenNoSecretIsConfigured()
    {
        await using WorkerApi api = await WorkerApi.StartAsync(secret: "");

        foreach (string endpoint in Endpoints)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: null));
            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: ""));
        }
    }

    [Fact]
    public async Task Endpoints_LetTheSecretThrough()
    {
        await using WorkerApi api = await WorkerApi.StartAsync(Secret);

        // There is no database behind this host, so the request fails past authorization rather than at it.
        foreach (string endpoint in Endpoints)
        {
            HttpStatusCode status = await api.PostAsync(endpoint, bearer: Secret);
            Assert.NotEqual(HttpStatusCode.Unauthorized, status);
            Assert.NotEqual(HttpStatusCode.Forbidden, status);
        }
    }

    [Fact]
    public async Task Endpoints_LetEveryNamedSecretThrough()
    {
        await using WorkerApi api = await WorkerApi.StartAsync(Secret, new Dictionary<string, string?>
        {
            ["Worker:Secrets:aws"] = AwsSecret,
            ["Worker:Secrets:short"] = "short-secret"
        });

        foreach (string endpoint in Endpoints)
        {
            foreach (string bearer in new[] { Secret, AwsSecret })
            {
                HttpStatusCode status = await api.PostAsync(endpoint, bearer);
                Assert.NotEqual(HttpStatusCode.Unauthorized, status);
                Assert.NotEqual(HttpStatusCode.Forbidden, status);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: "short-secret"));
            Assert.Equal(HttpStatusCode.Unauthorized, await api.PostAsync(endpoint, bearer: HomeSecret));
        }
    }

    private static WorkerOptions Named(string secret) => new()
    {
        Secret = secret,
        Secrets = { ["aws"] = AwsSecret, ["home"] = HomeSecret }
    };

    private static Task<AuthenticateResult> Authenticate(string configured, string? header) =>
        Authenticate(new WorkerOptions { Secret = configured }, header);

    private static async Task<AuthenticateResult> Authenticate(WorkerOptions workerOptions, string? header)
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/worker/claim";
        if (header is not null)
        {
            context.Request.Headers.Authorization = header;
        }

        WorkerAuthentication handler = new(
            new FixedOptions<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new FixedOptions<WorkerOptions>(workerOptions));
        await handler.InitializeAsync(
            new AuthenticationScheme(WorkerAuthentication.SchemeName, null, typeof(WorkerAuthentication)),
            context);
        return await handler.AuthenticateAsync();
    }

    private sealed class WorkerApi : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private WorkerApi(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<WorkerApi> StartAsync(string secret, Dictionary<string, string?>? settings = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings ?? [])
            {
                ["Worker:Secret"] = secret
            });

            // The site's default scheme stands in for a signed-in cookie: every request carries an account.
            builder.Services.AddAuthentication(SignedInSite.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, SignedInSite>(SignedInSite.SchemeName, null);
            builder.AddTranscodeWorkers();
            builder.AddReelshelfRateLimiting();

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapGroup("/api").MapWorkerEndpoints();
            await app.StartAsync();

            HttpClient client = new(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(app.Urls.First())
            };
            return new WorkerApi(app, client);
        }

        public async Task<HttpStatusCode> PostAsync(string path, string? bearer)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    """{"lease_id": "8d3c2f1e-0000-4000-8000-000000000000", "percent": 5, "error": "x", "retryable": true, "length_seconds": 1, "size_bytes": 1}""",
                    Encoding.UTF8, "application/json")
            };
            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            using HttpResponseMessage response = await _client.SendAsync(request);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class SignedInSite(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Site";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
