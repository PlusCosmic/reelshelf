using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
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
using Reelshelf.Users;
using Reelshelf.Exceptions;
using Reelshelf.FFmpeg;
using Reelshelf.Games;
using Reelshelf.Playlists;
using Reelshelf.Storage;
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
        // After authentication so per-user rate-limit partitions can key on the signed-in identity.
        app.UseRateLimiter();
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

        builder.AddReelshelfAuthentication();
        builder.AddReelshelfRateLimiting();
    }

    private static void AddApplicationModules(this WebApplicationBuilder builder)
    {
        // whitelist.json no longer gates access; it marks users with unlimited storage and role overrides.
        builder.Services.AddSingleton<WhitelistService>();
        builder.Services.AddScoped<StorageQuotaService>();
        builder.Services.AddSingleton<DiscordRoleMapping>();
        builder.Services.AddScoped<UserStatements>();
        builder.Services.AddScoped<IUserIdentityStore>(sp => sp.GetRequiredService<UserStatements>());
        builder.Services.AddScoped<AccountLinkingService>();
        builder.Services.AddScoped<GameCategoryStatements>();

        builder.Services.AddScoped<ClipsStatements>();
        builder.Services.AddScoped<ClipsBackfillStatements>();
        builder.Services.AddScoped<PlaylistStatements>();
        builder.Services.AddScoped<PlaylistAccess>();
        builder.Services.AddScoped<IGamingSessionPlaylistStore, GamingSessionPlaylistStore>();
        builder.Services.AddScoped<ClipProjection>();
        builder.Services.AddScoped<ClipService>();
        builder.Services.AddScoped<ClipLibraryService>();
        builder.Services.AddScoped<ClipsBackfillService>();
        builder.Services.AddScoped<PlaylistService>();
        builder.Services.AddScoped<GamingSessionPlaylistService>();
        builder.Services.AddScoped<AutoGeneratedGamingSessionPlaylistService>();
        builder.Services.AddScoped<BunnyService>();
        builder.Services.AddScoped<FFmpegService>();

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IgdbService>();
        builder.Services.AddScoped<GameCategoryService>();

        builder.Services.AddScoped<ApexStatements>();
        builder.Services.AddScoped<MapService>();
        builder.Services.AddScoped<IApexMapCacheService, ApexMapCacheService>();
        builder.Services.AddScoped<IApexDetectionQueueService, ApexDetectionQueueService>();
        builder.Services.AddScoped<ApexDetectionWorkflow>();

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
