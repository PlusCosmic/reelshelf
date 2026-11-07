using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Npgsql;
using Reelshelf.ApexLegends;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.ClipBin;
using Reelshelf.FeatureFlags;
using Reelshelf.ClipSummary;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;
using Reelshelf.Devices;
using Reelshelf.Discord.DiscordActivity;
using Reelshelf.Email;
using Reelshelf.Users;
using Reelshelf.Exceptions;
using Reelshelf.FFmpeg;
using Reelshelf.Games;
using Reelshelf.LandingShelf;
using Reelshelf.Moderation;
using Reelshelf.Playlists;
using Reelshelf.QueueMetrics;
using Reelshelf.Plans;
using Reelshelf.Twitch;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;
using Reelshelf.Video.Transcoding.Overflow;
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
            app.UseStaticFiles(new StaticFileOptions
            {
                // Vite fingerprints everything under /assets/, so a changed file always has a new name.
                OnPrepareResponse = context =>
                {
                    if (context.Context.Request.Path.StartsWithSegments("/assets"))
                    {
                        context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                    }
                }
            });
        }

        app.UseExceptionHandler();
        app.UseCors();
        // An uploader too old for this API is told so before anything else runs (ADR-0010).
        app.UseClientVersionGate();
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
        apiGroup.MapClipBinEndpoints();
        apiGroup.MapMultipartUploadEndpoints();
        apiGroup.MapSharedClipsEndpoints();
        apiGroup.MapContentReportEndpoints();
        apiGroup.MapPlaylistEndpoints();
        apiGroup.MapFFmpegEndpoints();
        apiGroup.MapGameCategoryEndpoints();
        apiGroup.MapLandingShelfEndpoints();
        apiGroup.MapLegendDetectionEndpoints();
        apiGroup.MapClipTranscriptionEndpoints();
        apiGroup.MapClipSummaryEndpoints();
        apiGroup.MapUserEndpoints();
        apiGroup.MapAdminAccountEndpoints();
        apiGroup.MapBillingEndpoints();
        apiGroup.MapPlanEndpoints();
        apiGroup.MapOverLimitEndpoints();
        apiGroup.MapTwitchClipsEndpoints();
        apiGroup.MapDiscordActivityEndpoints();
        apiGroup.MapWorkerEndpoints();
        apiGroup.MapDeviceEndpoints();

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

    /// <summary>
    /// Redis, from <c>RedisConnectionString</c> (StackExchange.Redis's format, <c>localhost:6379</c> when unset).
    /// It never refuses to start without Redis: the multiplexer keeps trying to connect in the background.
    /// </summary>
    internal static ConfigurationOptions RedisOptions(IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("RedisConnectionString")
                                  ?? configuration["RedisConnectionString"]
                                  ?? "localhost:6379";
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return options;
    }

    private static void AddPersistence(this WebApplicationBuilder builder)
    {
        string? connectionString = builder.Configuration.GetConnectionString("DatabaseConnectionString")
                                   ?? builder.Configuration["DatabaseConnectionString"];

        NpgsqlDataSource dataSource = ReelshelfDataSource.Create(connectionString ??
            "Host=localhost;Database=reelshelf_db;Username=reelshelf_user;Password=dummy");

        builder.Services.AddSingleton(dataSource);
        builder.Services.AddScoped(_ => dataSource.CreateConnection());

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(RedisOptions(builder.Configuration)));

        IHealthChecksBuilder healthChecksBuilder = builder.Services.AddHealthChecks();

        if (!string.IsNullOrEmpty(connectionString))
        {
            // Its own named pool, as before (the connection-string overload made an unnamed one), so Npgsql's pool
            // metrics never carry the connection string.
            NpgsqlDataSource healthCheckDataSource = ReelshelfDataSource.CreateHealthCheck(connectionString);
            healthChecksBuilder.AddNpgSql(
                _ => healthCheckDataSource,
                name: "database",
                timeout: TimeSpan.FromSeconds(3),
                tags: ["ready"]);
        }
    }

    private static void AddAuthenticationAndAuthorization(this WebApplicationBuilder builder)
    {
        IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("Reelshelf");

        // Without an explicit path the keys go to the framework default ($HOME/.aspnet/DataProtection-Keys),
        // never the content root: a dev server serving the checkout would otherwise expose them.
        string? keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        builder.AddReelshelfAuthentication();
        builder.AddDiscordActivity();
        builder.AddTranscodeWorkers();
        // The desktop uploader's device tokens (ADR-0010), accepted only where CookieOrDevice is required.
        builder.AddDevices();
        builder.AddReelshelfRateLimiting();
    }

    private static void AddApplicationModules(this WebApplicationBuilder builder)
    {
        // Roles and granted tiers are stored on the account (ADR-0008); AccountAccessService alone changes them.
        builder.Services.AddScoped<IAccountAccessStore, AccountAccessStatements>();
        builder.Services.AddScoped<AccountAccessService>();
        builder.Services.AddScoped<AccountPlanService>();
        builder.AddPlanEnforcement();
        builder.Services.AddSingleton<ProviderTokenProtector>();
        builder.Services.AddScoped<UserStatements>();
        builder.Services.AddScoped<IUserIdentityStore>(sp => sp.GetRequiredService<UserStatements>());
        builder.Services.AddScoped<IProviderTokenStore>(sp => sp.GetRequiredService<UserStatements>());
        builder.Services.AddScoped<AccountLinkingService>();
        builder.Services.AddScoped<IClipLimitWarningStore>(sp => sp.GetRequiredService<UserStatements>());
        builder.Services.AddScoped<ClipLimitWarningService>();
        builder.AddBilling();
        // Feature flags (ADR-0009): PostHog in prod, off elsewhere unless FeatureFlags:Overrides turns one on.
        builder.AddFeatureFlags();
        builder.Services.AddSingleton<AccountDeletionSignal>();
        builder.Services.AddScoped<IAccountDeletionStore, AccountDeletionStatements>();
        builder.Services.AddScoped<AccountDeletionService>();

        string? resendApiKey = builder.Configuration["Resend:ApiKey"] ?? builder.Configuration["ResendApiKey"];
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            builder.Services.AddHttpClient(ResendEmailSender.HttpClientName,
                client => ResendEmailSender.Configure(client, resendApiKey));
            builder.Services.AddSingleton<IEmailSender, ResendEmailSender>();
        }
        else
        {
            builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        builder.Services.AddScoped<GameCategoryStatements>();

        builder.Services.AddScoped<ClipsStatements>();
        builder.AddModeration();
        builder.Services.AddScoped<PlaylistStatements>();
        builder.Services.AddScoped<PlaylistAccess>();
        builder.Services.AddScoped<IGamingSessionPlaylistStore, GamingSessionPlaylistStore>();
        builder.Services.AddScoped<ClipProjection>();
        builder.Services.AddSingleton<ClipMediaUrls>();
        builder.Services.AddScoped<ClipService>();
        builder.Services.AddScoped<ClipMoveService>();
        // The bin (V55): deleted clips wait there for Bin:Retention before the sweep deletes them for good.
        builder.Services.Configure<ClipBinOptions>(builder.Configuration.GetSection(ClipBinOptions.SectionName));
        builder.Services.AddScoped<ClipBinService>();
        builder.Services.AddScoped<ClipBinSweeper>();
        builder.Services.AddScoped<ClipLibraryService>();
        builder.Services.AddScoped<PlaylistService>();
        builder.Services.AddScoped<GamingSessionPlaylistService>();
        builder.Services.AddScoped<AutoGeneratedGamingSessionPlaylistService>();
        // Each clip records its host in clip.video_host; adding a host is implementing IClipVideoHost and
        // IClipVideoMediaUrls and registering them under its name. R2 is the only one, and is required outside the
        // OpenApi environment.
        builder.Services.AddR2VideoHost(builder.Configuration);
        builder.Services.AddClipVideoHostRouting(requireR2: !builder.Environment.IsEnvironment("OpenApi"));
        builder.Services.AddScoped<ClipVideoUpdateHandler>();

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IgdbService>();

        // Twitch clip import: Helix calls on the user's token, plus a patient client for the clip file itself.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(TwitchApiClient.HttpClientName);
        builder.Services.AddHttpClient(TwitchClipService.DownloadHttpClientName,
            client => client.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddSingleton<TwitchApiClient>();
        builder.Services.AddScoped<TwitchClipService>();
        builder.Services.AddScoped<GameCategoryService>();
        builder.Services.AddHttpClient(GameCoverColors.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddScoped<GameCoverColors>();
        // Copies of IGDB categories' images in R2, served from the media domain; without R2, IGDB's own URLs.
        builder.Services.AddSingleton(provider =>
            new GameArtUrls(provider.GetService<R2Options>()?.MediaBaseUrl));
        builder.Services.AddHttpClient(GameArtMirror.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddScoped(provider => new GameArtMirror(
            provider.GetService<R2Storage>() is { } r2 ? new R2GameArtStore(r2) : null,
            provider.GetRequiredService<GameCategoryStatements>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<GameArtMirror>>()));

        // The landing page's example shelf: IGDB's most popular games, fetched daily into memory.
        builder.Services.Configure<LandingShelfOptions>(
            builder.Configuration.GetSection(LandingShelfOptions.SectionName));
        builder.Services.AddSingleton<IPopularGameSource>(sp => sp.GetRequiredService<IgdbService>());
        builder.Services.AddSingleton<LandingShelfCache>();
        builder.Services.AddScoped<LandingShelfRefresher>();

        // Legend detection stays idle until a provider API key and model are configured.
        builder.Services.Configure<LegendDetectionOptions>(
            builder.Configuration.GetSection(LegendDetectionOptions.SectionName));
        builder.Services.AddSingleton<LegendDetectionResources>();
        builder.Services.AddSingleton<ILegendRecognizerFactory, LegendRecognizerFactory>();
        builder.Services.AddScoped<LegendDetectionStatements>();
        builder.Services.AddScoped<LegendDetectionService>();
        builder.Services.AddHttpClient(LegendDetectionService.FramesHttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddHttpClient(DecisionsLegendRecognizer.HttpClientName,
            client => client.Timeout = TimeSpan.FromMinutes(2));
        builder.Services.AddHttpClient(OpenRouterDecisionsLegendRecognizer.HttpClientName,
            client => client.Timeout = TimeSpan.FromMinutes(2));
        // The Anthropic SDK applies its own timeout per attempt, so the HttpClient's must not cut it short.
        builder.Services.AddHttpClient(LegendRecognizerFactory.AnthropicHttpClientName,
            client => client.Timeout = Timeout.InfiniteTimeSpan);

        // Clip transcription stays idle until ClipTranscription:OpenRouterApiKey is configured.
        builder.Services.Configure<ClipTranscriptionOptions>(
            builder.Configuration.GetSection(ClipTranscriptionOptions.SectionName));
        builder.Services.AddSingleton<IClipTranscriber, OpenRouterClipTranscriber>();
        builder.Services.AddScoped<ClipTranscriptionStatements>();
        builder.Services.AddScoped<ClipTranscriptionService>();
        builder.Services.AddHttpClient(OpenRouterClipTranscriber.HttpClientName,
            client => client.Timeout = TimeSpan.FromMinutes(5));

        // Clip summaries stay idle until ClipSummary:Providers:openai:ApiKey is configured.
        builder.Services.Configure<ClipSummaryOptions>(
            builder.Configuration.GetSection(ClipSummaryOptions.SectionName));
        builder.Services.AddSingleton<ClipSummaryResources>();
        builder.Services.AddSingleton<IClipSummarizerFactory, ClipSummarizerFactory>();
        builder.Services.AddScoped<ClipSummaryStatements>();
        builder.Services.AddScoped<ClipSummaryService>();

        // Gauges for every run table above; the sampler stays out of the OpenApi environment like the services below.
        builder.AddQueueMetrics();

        // Overflow workers for the transcode queue (Fargate Spot and/or Google Cloud Batch, Overflow:Pools); only where
        // Overflow:Enabled is on, never in OpenApi.
        builder.AddTranscodeOverflow();

        // Background services should not run during explicit OpenAPI document generation.
        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<ClipStatusRefreshService>();
            builder.Services.AddHostedService<GameCategoryAssetRefreshService>();
            builder.Services.AddHostedService<GameCategoryClothColorService>();
            builder.Services.AddHostedService<LandingShelfRefreshService>();
            builder.Services.AddHostedService<LegendDetectionBackgroundService>();
            builder.Services.AddHostedService<ClipTranscriptionBackgroundService>();
            builder.Services.AddHostedService<ClipSummaryBackgroundService>();
            builder.Services.AddHostedService<AccountDeletionBackgroundService>();
            builder.Services.AddHostedService<ClipBinSweepService>();
        }
    }
}
