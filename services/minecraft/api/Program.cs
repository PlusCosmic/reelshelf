using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Npgsql;
using Nucleus.Minecraft.Auth;
using Nucleus.Minecraft.Data;
using Nucleus.Minecraft.Discord;
using Nucleus.Minecraft.Endpoints;
using Nucleus.Minecraft.Models;
using Nucleus.Minecraft.Services;
using Nucleus.Shared.Auth;
using Nucleus.Shared.Discord;
using Nucleus.Shared.Exceptions;

DefaultTypeMap.MatchNamesWithUnderscores = true;
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register services
builder.RegisterServices();
builder.RegisterDatabase();
builder.ConfigureDiscordAuth();

WebApplication app = builder.Build();

app.UseHttpsRedirection();

// Static file serving for SPA - must be before auth when a frontend is published with the API.
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseExceptionHandler();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WhitelistMiddleware>();
app.UseAuthenticatedUserResolution();


// Auth endpoints stay at root level (not under /api) for OAuth callback compatibility
app.MapAuthEndpoints(builder.Configuration);

// Create API group with /api prefix for all other endpoints
RouteGroupBuilder apiGroup = app.MapGroup("/api");
apiGroup.MapMinecraftEndpoints();
apiGroup.MapUserEndpoints();

// OpenAPI is useful during development and explicit client-generation jobs, but should not be public by default.
if (app.Environment.IsDevelopment() ||
    app.Environment.IsEnvironment("OpenApi") ||
    app.Configuration.GetValue<bool>("OpenApi:Public"))
{
    app.MapOpenApi();
}
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    AllowCachingResponses = false,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();

// SPA fallback - serve index.html for client-side routing when frontend assets are present.
if (File.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program
{
}

public static class BuilderExtensions
{
    public static void RegisterServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi(options =>
        {
            // Fix for OpenAPI 3.1 nullable type arrays that break typescript-fetch generator.
            // When a schema has type: ["null", "object"], the generator incorrectly creates
            // references to a non-existent "Null" type. This transformer removes the Null flag
            // from schema definitions so they generate as pure object types.
            options.AddSchemaTransformer((schema, context, cancellationToken) =>
            {
                if (schema.Type.HasValue &&
                    schema.Type.Value.HasFlag(JsonSchemaType.Null) &&
                    schema.Type.Value.HasFlag(JsonSchemaType.Object))
                {
                    schema.Type = JsonSchemaType.Object;
                }

                return Task.CompletedTask;
            });
        });
        builder.Services.AddHttpClient();

        // Shared services from Nucleus.Shared
        builder.Services.AddScoped<DiscordStatements>();
        builder.Services.AddSingleton<WhitelistService>();

        // Minecraft services
        builder.Services.AddScoped<MinecraftStatements>();
        builder.Services.AddSingleton<RconService>();
        builder.Services.AddSingleton<DockerContainerService>();
        builder.Services.AddScoped<MinecraftStatusService>();
        builder.Services.AddScoped<FileService>();
        builder.Services.AddSingleton<LogTailerService>();
        builder.Services.AddScoped<ConsoleWebSocketHandler>();
        builder.Services.AddScoped<BackupService>();
        // Background services should not run during explicit OpenAPI document generation.
        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<BackupSyncBackgroundService>();
        }

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                                  ?? builder.Configuration.GetSection("Auth:AllowedReturnOrigins").Get<string[]>()
                                  ?? [];

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policyBuilder =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policyBuilder.WithOrigins(allowedOrigins);
                }
                else if (builder.Environment.IsDevelopment())
                {
                    policyBuilder.SetIsOriginAllowed(origin =>
                        Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) &&
                        (uri.Host == "localhost" || uri.Host == "127.0.0.1"));
                }

                policyBuilder
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .AllowAnyHeader();
            });
        });
    }

    public static void RegisterDatabase(this WebApplicationBuilder builder)
    {
        string? connectionString = builder.Configuration.GetConnectionString("DatabaseConnectionString")
                                   ?? builder.Configuration["DatabaseConnectionString"];

        NpgsqlDataSourceBuilder dataSourceBuilder = new(connectionString ??
            "Host=localhost;Database=nucleus_db;Username=nucleus_user;Password=dummy");
        dataSourceBuilder.MapEnum<MinecraftServerType>("minecraft_server_type");
        NpgsqlDataSource dataSource = dataSourceBuilder.Build();

        builder.Services.AddSingleton(dataSource);
        builder.Services.AddScoped(_ => dataSource.CreateConnection());

        IHealthChecksBuilder healthChecksBuilder = builder.Services.AddHealthChecks();

        if (!string.IsNullOrEmpty(connectionString))
        {
            healthChecksBuilder.AddNpgSql(
                connectionString,
                name: "database",
                timeout: TimeSpan.FromSeconds(3),
                tags: ["ready"]);
        }
    }

    public static void ConfigureDiscordAuth(this WebApplicationBuilder builder)
    {
        string? discordClientId = builder.Configuration["DiscordClientId"];
        string? discordClientSecret = builder.Configuration["DiscordClientSecret"];
        string keysPath = builder.Configuration["DataProtection:KeysPath"]
                          ?? Path.Combine(builder.Environment.ContentRootPath, "keys");

        builder.Services.AddDataProtection()
            .SetApplicationName("Nucleus.Minecraft")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "pcdash.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.Cookie.MaxAge = TimeSpan.FromDays(7);

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOAuth("Discord", options =>
            {
                options.ClientId = discordClientId!;
                options.ClientSecret = discordClientSecret!;
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                options.AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize";
                options.TokenEndpoint = "https://discord.com/api/oauth2/token";
                options.UserInformationEndpoint = "https://discord.com/api/users/@me";

                options.CallbackPath = "/auth/discord/callback";

                options.Scope.Clear();
                options.Scope.Add("identify");

                options.SaveTokens = true;

                options.ClaimActions.Clear();
                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.ClaimActions.MapJsonKey("urn:discord:global_name", "global_name");
                options.ClaimActions.MapJsonKey("urn:discord:discriminator", "discriminator");
                options.ClaimActions.MapJsonKey("urn:discord:avatar", "avatar");

                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = async ctx =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                        using var response = await ctx.Backchannel.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

                        ctx.RunClaimActions(json);
                    },
                    OnRemoteFailure = ctx =>
                    {
                        var loggerFactory = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("Nucleus.Minecraft.Auth.DiscordOAuth");

                        if (ctx.Failure?.Message?.Contains("correlation") == true ||
                            ctx.Failure?.Message?.Contains("state") == true)
                        {
                            logger.LogWarning("OAuth state validation failed - possible CSRF attack: {Error}", ctx.Failure.Message);
                        }
                        else
                        {
                            logger.LogError("OAuth authentication failed: {Error}", ctx.Failure?.Message ?? "Unknown error");
                        }

                        ctx.Response.Redirect("/auth/login-failed");
                        ctx.HandleResponse();
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();
    }
}
