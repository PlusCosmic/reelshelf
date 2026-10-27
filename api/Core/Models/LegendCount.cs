namespace Reelshelf.Core.Models;

/// <summary>An Apex legend the owner played, spelled as in <c>ApexLegendNames</c>, and in how many of their clips.</summary>
public record LegendCount(string Legend, int Count);
