using Nucleus.Clips.ApexLegends.Models;
using Nucleus.Shared.Exceptions;

namespace Nucleus.Clips.ApexLegends;

public class MapService(IApexMapCacheService cacheService, IConfiguration configuration)
{
    public async Task<CurrentMapRotation> GetMapRotation()
    {
        CurrentMapRotation? rotation = await cacheService.GetMapRotationAsync();
        if (rotation == null)
        {
            throw new ServiceUnavailableException("Apex Legends Status Unavailable");
        }

        return rotation;
    }

    public CurrentMapRotation ProcessApiResponse(MapRotationResponse response)
    {
        MapInfo standardCurrent = MapRotationInfoToMapInfo(response.BattleRoyale.Current);
        MapInfo standardNext = MapRotationInfoToMapInfo(response.BattleRoyale.Next);
        MapInfo rankedCurrent = MapRotationInfoToMapInfo(response.Ranked.Current);
        MapInfo rankedNext = MapRotationInfoToMapInfo(response.Ranked.Next);

        return new CurrentMapRotation(
            standardCurrent,
            standardNext,
            rankedCurrent,
            rankedNext,
            DateTimeOffset.UtcNow);
    }

    private string GetFriendlyNameForMap(ApexMap map)
    {
        return map switch
        {
            ApexMap.KingsCanyon => "Kings Canyon",
            ApexMap.EDistrict => "E-District",
            ApexMap.Olympus => "Olympus",
            ApexMap.StormPoint => "Storm Point",
            ApexMap.BrokenMoon => "Broken Moon",
            ApexMap.WorldsEdge => "Worlds Edge",
            _ => throw new InvalidOperationException($"Unknown map: {map}")
        };
    }

    private Uri GetAssetUriForMap(ApexMap map)
    {
        string? start = configuration["BackendAddress"];
        if (start == null)
        {
            throw new InvalidOperationException("Backend address not configured");
        }

        string filename = map switch
        {
            ApexMap.KingsCanyon => "kings-canyon.avif",
            ApexMap.WorldsEdge => "worlds-edge.avif",
            ApexMap.Olympus => "olympus.avif",
            ApexMap.StormPoint => "storm-point.avif",
            ApexMap.BrokenMoon => "broken-moon.avif",
            ApexMap.EDistrict => "e-district.avif",
            _ => throw new ArgumentOutOfRangeException(nameof(map), map, null)
        };
        return new Uri($"{start}/images/{filename}");
    }

    private MapInfo MapRotationInfoToMapInfo(MapRotationInfo info)
    {
        ApexMap map = MapCodeToEnum(info.Code);
        return new MapInfo(GetFriendlyNameForMap(map), info.StartTime, info.EndTime, GetAssetUriForMap(map));
    }

    private static ApexMap MapCodeToEnum(string mapCode)
    {
        return mapCode switch
        {
            "kings_canyon_rotation" => ApexMap.KingsCanyon,
            "edistrict_rotation" => ApexMap.EDistrict,
            "olympus_rotation" => ApexMap.Olympus,
            "worlds_edge_rotation" => ApexMap.WorldsEdge,
            "storm_point_rotation" => ApexMap.StormPoint,
            "broken_moon_rotation" => ApexMap.BrokenMoon,
            _ => throw new InvalidOperationException($"Unknown map code: {mapCode}")
        };
    }
}