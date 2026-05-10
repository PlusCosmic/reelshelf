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
using Reelshelf.ApexLegends;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Auth;
using Reelshelf.Bunny;
using Reelshelf.Core;
using Reelshelf.Discord;
using Reelshelf.Exceptions;
using Reelshelf.FFmpeg;
using Reelshelf.Games;
using Reelshelf.Playlists;
using StackExchange.Redis;

namespace Reelshelf;

internal static class ReelshelfApiConfiguration
{
    public static void AddReelshelfApi(this WebApplicationBuilder builder)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        builder.AddApiContract();
        builder.AddPersistence();
        builder.AddAuthenticationAndAuthorization();
        builder.AddApplicationModules();
    }

    public static void UseReelshelfApi(this WebApplication app)
    {
        app.UseHttpsRedirection();

        // Static file serving for SPA - must be before auth when a frontend is published with the API.
        if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        app.UseExceptionHandler();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<WhitelistMiddleware>();
        app.UseAuthenticatedUserResolution();
    }

    public static void MapReelshelfApi(this WebApplication app)
    {
        // Auth endpoints stay at root level (not under /api) for OAuth callback compatibility.
        app.MapAuthEndpoints(app.Configuration);

        RouteGroupBuilder apiGroup = app.MapGroup("/api");
        apiGroup.MapClipsEndpoints();
        apiGroup.MapSharedClipsEndpoints();
        apiGroup.MapPlaylistEndpoints();
        apiGroup.MapFFmpegEndpoints();
        apiGroup.MapBunnyWebhookEndpoints();
        apiGroup.MapGameCategoryEndpoints();
        apiGroup.MapApexEndpoints();
        apiGroup.MapApexDetectionEndpoints();
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
    }

    private static void AddApiContract(this WebApplicationBuilder builder)
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

    private static void AddPersistence(this WebApplicationBuilder builder)
    {
        string? connectionString = builder.Configuration.GetConnectionString("DatabaseConnectionString")
                                   ?? builder.Configuration["DatabaseConnectionString"];

        string? redisConnectionString = builder.Configuration.GetConnectionString("RedisConnectionString")
                                        ?? builder.Configuration["RedisConnectionString"]
                                        ?? "localhost:6379";

        NpgsqlDataSourceBuilder dataSourceBuilder = new(connectionString ??
            "Host=localhost;Database=reelshelf_db;Username=reelshelf_user;Password=dummy");
        NpgsqlDataSource dataSource = dataSourceBuilder.Build();

        builder.Services.AddSingleton(dataSource);
        builder.Services.AddScoped(_ => dataSource.CreateConnection());

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect($"{redisConnectionString},abortConnect=false"));

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

    private static void AddAuthenticationAndAuthorization(this WebApplicationBuilder builder)
    {
        string keysPath = builder.Configuration["DataProtection:KeysPath"]
                          ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
        builder.Services.AddDataProtection()
            .SetApplicationName("Reelshelf")
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
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddOAuth("Discord", options =>
            {
                options.AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize";
                options.TokenEndpoint = "https://discord.com/api/oauth2/token";
                options.UserInformationEndpoint = "https://discord.com/api/users/@me";

                options.ClientId = builder.Configuration["DiscordClientId"] ?? "";
                options.ClientSecret = builder.Configuration["DiscordClientSecret"] ?? "";

                options.CallbackPath = new PathString("/auth/discord/callback");

                options.Scope.Add("identify");

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.ClaimActions.MapJsonKey("urn:discord:avatar", "avatar");
                options.ClaimActions.MapJsonKey("urn:discord:global_name", "global_name");

                options.Events = new OAuthEvents
                {
                    OnRedirectToAuthorizationEndpoint = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnCreatingTicket = async context =>
                    {
                        HttpRequestMessage request = new(HttpMethod.Get, context.Options.UserInformationEndpoint);
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", context.AccessToken);

                        HttpResponseMessage response = await context.Backchannel.SendAsync(request);
                        response.EnsureSuccessStatusCode();

                        JsonDocument user = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                        context.RunClaimActions(user.RootElement);
                    }
                };

                options.SaveTokens = true;
            });

        builder.Services.AddAuthorization();
    }

    private static void AddApplicationModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<WhitelistService>();
        builder.Services.AddSingleton<DiscordRoleMapping>();
        builder.Services.AddScoped<DiscordStatements>();
        builder.Services.AddScoped<GameCategoryStatements>();

        builder.Services.AddScoped<ClipsStatements>();
        builder.Services.AddScoped<ClipsBackfillStatements>();
        builder.Services.AddScoped<PlaylistStatements>();
        builder.Services.AddScoped<ClipProjection>();
        builder.Services.AddScoped<ClipService>();
        builder.Services.AddScoped<ClipsBackfillService>();
        builder.Services.AddScoped<PlaylistService>();
        builder.Services.AddScoped<BunnyService>();
        builder.Services.AddScoped<FFmpegService>();

        builder.Services.AddScoped<IgdbService>();
        builder.Services.AddScoped<GameCategoryService>();

        builder.Services.AddScoped<ApexStatements>();
        builder.Services.AddScoped<MapService>();
        builder.Services.AddScoped<IApexMapCacheService, ApexMapCacheService>();
        builder.Services.AddScoped<IApexDetectionQueueService, ApexDetectionQueueService>();

        // Background services should not run during explicit OpenAPI document generation.
        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<ClipStatusRefreshService>();
            builder.Services.AddHostedService<MapRefreshService>();
            builder.Services.AddHostedService<GameCategoryAssetRefreshService>();
            builder.Services.AddHostedService<ApexDetectionBackgroundService>();
        }
    }
}
