using Nucleus.Clips.ApexLegends.Models;

namespace Nucleus.Clips.ApexLegends;

public class MapRefreshService(
    ILogger<MapRefreshService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    HttpClient httpClient)
    : BackgroundService
{
    private readonly string? _apiKey = configuration["ApexLegendsApiKey"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogWarning("ApexLegendsApiKey not configured - map refresh service disabled");
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        await RefreshMapsAsync();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshMapsAsync();
        }
    }

    private async Task RefreshMapsAsync()
    {
        logger.LogInformation("Refreshing Map Rotation");

        try
        {
            string mapUrl = $"https://api.mozambiquehe.re/maprotation?version=2&auth={_apiKey}";
            HttpResponseMessage mapRotation = await httpClient.GetAsync(mapUrl);
            if (!mapRotation.IsSuccessStatusCode)
            {
                logger.LogError("Failed to refresh map rotation: HTTP {StatusCode}", mapRotation.StatusCode);
                return;
            }

            MapRotationResponse? response = await mapRotation.Content.ReadFromJsonAsync<MapRotationResponse>();
            if (response == null)
            {
                logger.LogError("Failed to deserialize map rotation response");
                return;
            }

            using IServiceScope scope = scopeFactory.CreateScope();
            MapService mapService = scope.ServiceProvider.GetRequiredService<MapService>();
            IApexMapCacheService cacheService = scope.ServiceProvider.GetRequiredService<IApexMapCacheService>();

            CurrentMapRotation processedRotation = mapService.ProcessApiResponse(response);
            await cacheService.SetMapRotationAsync(processedRotation);

            logger.LogInformation("Successfully cached map rotation data");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to refresh and cache map rotation");
        }
    }
}