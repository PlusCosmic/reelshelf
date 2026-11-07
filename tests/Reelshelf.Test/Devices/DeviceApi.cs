using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Devices;
using Reelshelf.Test.Video;
using Reelshelf.Video.R2;

namespace Reelshelf.Test.Devices;

/// <summary>
/// The whole API as <c>Program</c> builds it, served by Kestrel on a loopback port against a schema of its own with
/// every migration applied, R2 replaced by <see cref="FakeR2Client"/>, and none of the app's background services.
/// Requests carry the site cookie (made with the app's own cookie protector, as signing in would) or a device token.
/// </summary>
internal sealed class DeviceApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;

    private DeviceApi(WebApplication app, HttpClient client, NpgsqlDataSource dataSource, NpgsqlConnection connection,
        string schema, FakeR2Client r2)
    {
        _app = app;
        Client = client;
        _dataSource = dataSource;
        Connection = connection;
        _schema = schema;
        R2 = r2;
    }

    public HttpClient Client { get; }

    public NpgsqlConnection Connection { get; }

    public FakeR2Client R2 { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<DeviceApi> StartAsync(IDictionary<string, string?>? settings = null)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"device_test_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await PostgresFactAttribute.CreateExtensionsAsync(admin, "vector", "pgcrypto");
            await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        }

        // public stays on the path for the extensions' types.
        string connectionString = $"{PostgresFactAttribute.ConnectionString};Search Path={schema},public";
        NpgsqlDataSource dataSource = ReelshelfDataSource.Create(connectionString);
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        // The baseline creates this type only when no schema has one yet, so other tests' schemas would hide it.
        await connection.ExecuteAsync(
            "CREATE TYPE minecraft_server_type AS ENUM ('vanilla', 'fabric', 'neoforge', 'curseforge')");
        foreach (string migration in Migrations())
        {
            await connection.ExecuteAsync(await File.ReadAllTextAsync(migration));
        }

        await connection.ReloadTypesAsync();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        Dictionary<string, string?> configuration = new()
        {
            ["DatabaseConnectionString"] = connectionString,
            ["DiscordClientId"] = "test",
            ["DiscordClientSecret"] = "test",
            ["TwitchClientId"] = "test",
            ["TwitchClientSecret"] = "test",
            ["GoogleClientId"] = "test",
            ["GoogleClientSecret"] = "test",
            ["R2:AccountId"] = FakeR2Client.Options.AccountId,
            ["R2:AccessKeyId"] = FakeR2Client.Options.AccessKeyId,
            ["R2:SecretAccessKey"] = FakeR2Client.Options.SecretAccessKey,
            ["R2:Bucket"] = FakeR2Client.Options.Bucket,
            ["R2:MediaBaseUrl"] = FakeR2Client.Options.MediaBaseUrl,
            ["FrontendOrigin"] = "https://reelshelf.test",
            ["FeatureFlags:Overrides:desktop-uploader"] = "true",
            ["Bin:SweepEnabled"] = "false"
        };
        foreach ((string key, string? value) in settings ?? new Dictionary<string, string?>())
        {
            configuration[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddReelshelfApi();

        // None of the app's own background services: the tests run the sweep themselves.
        foreach (ServiceDescriptor hosted in builder.Services
                     .Where(service => service.ServiceType == typeof(IHostedService) &&
                                       service.ImplementationType?.Assembly == typeof(DeviceStatements).Assembly)
                     .ToList())
        {
            builder.Services.Remove(hosted);
        }

        FakeR2Client r2 = new();
        builder.Services.AddSingleton(r2.Storage());

        WebApplication app = builder.Build();
        app.UseReelshelfApi();
        app.MapReelshelfApi();
        await app.StartAsync();

        HttpClient client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(app.Urls.First())
        };
        return new DeviceApi(app, client, dataSource, connection, schema, r2);
    }

    /// <summary>Every migration in version order.</summary>
    private static IEnumerable<string> Migrations()
    {
        string directory = Path.GetDirectoryName(TestClipSchema.MigrationPath("V63__device_tokens.sql"))!;
        return Directory.GetFiles(directory, "V*__*.sql")
            .OrderBy(path => int.Parse(Regex.Match(Path.GetFileName(path), @"^V(\d+)__").Groups[1].Value));
    }

    /// <summary>An Editor account with an email address, so it has finished onboarding.</summary>
    public async Task<Guid> AddUserAsync(string username = "player")
    {
        Guid id = Guid.NewGuid();
        await Connection.ExecuteAsync("""
            INSERT INTO app_user (id, username, email, role) VALUES (@id, @username, @email, 'Editor')
            """, new { id, username = $"{username}_{id:N}"[..20], email = $"{id:N}@example.test" });
        return id;
    }

    /// <summary>A book on the account's shelf.</summary>
    public async Task<Guid> AddBookAsync(Guid userId, string name)
    {
        Guid id = Guid.NewGuid();
        await Connection.ExecuteAsync("""
            INSERT INTO game_category (id, name, slug, is_custom) VALUES (@id, @name, @slug, true);
            INSERT INTO user_game_category (user_id, game_category_id) VALUES (@userId, @id);
            """, new { id, name, slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{id:N}", userId });
        return id;
    }

    /// <summary>A clip of the account's in the given state.</summary>
    public async Task<Guid> AddClipAsync(Guid userId, Guid bookId, string videoState)
    {
        Guid id = Guid.NewGuid();
        await Connection.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host)
            VALUES (@id, @userId, @id, @bookId, 'Clip', @videoState, 'r2')
            """, new { id, userId, bookId, videoState });
        return id;
    }

    /// <summary>The value of the site cookie for the account, as signing in sets it.</summary>
    public string CookieFor(Guid userId)
    {
        CookieAuthenticationOptions options = _app.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme));
        AuthenticationTicket ticket = new(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? cookieUser = null,
        string? deviceToken = null, object? json = null, string? client = null)
    {
        HttpRequestMessage request = new(method, path);
        if (cookieUser is { } userId)
        {
            request.Headers.Add("Cookie", CookieFor(userId));
        }

        if (deviceToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deviceToken);
        }

        if (client is not null)
        {
            request.Headers.Add(DeviceTokenAuthentication.ClientHeader, client);
        }

        if (json is not null)
        {
            request.Content = JsonContent.Create(json);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>Signs a device in for the account the way the app and <c>/device</c> do, and answers its token.</summary>
    public async Task<string> SignInDeviceAsync(Guid userId, string name = "Gaming PC")
    {
        using HttpResponseMessage authorize = await SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = name });
        authorize.EnsureSuccessStatusCode();
        DeviceAuthorizationJson started = (await authorize.Content.ReadFromJsonAsync<DeviceAuthorizationJson>())!;

        using HttpResponseMessage approve = await SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: userId);
        approve.EnsureSuccessStatusCode();

        using HttpResponseMessage token = await SendAsync(HttpMethod.Post, "/api/devices/token",
            json: new { device_code = started.device_code });
        token.EnsureSuccessStatusCode();
        return (await token.Content.ReadFromJsonAsync<DeviceTokenJson>())!.access_token;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
        }
        finally
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
        }
    }

    // Snake case as the API sends it.
#pragma warning disable IDE1006
    public sealed record DeviceAuthorizationJson(
        string device_code,
        string user_code,
        string verification_uri,
        string verification_uri_complete,
        int interval,
        int expires_in);

    public sealed record DeviceTokenJson(string access_token, string token_type);

    public sealed record DeviceTokenErrorJson(string error);
#pragma warning restore IDE1006
}
