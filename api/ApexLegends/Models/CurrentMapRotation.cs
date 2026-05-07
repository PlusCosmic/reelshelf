namespace Reelshelf.ApexLegends.Models;

public record CurrentMapRotation(MapInfo StandardMap, MapInfo StandardMapNext, MapInfo RankedMap, MapInfo RankedMapNext, DateTimeOffset CorrectAsOf);