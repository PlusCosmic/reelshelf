namespace Nucleus.Clips.ApexLegends.Models;

public enum ApexLegend
{
    Valkyrie,
    Horizon,
    Revenant,
    MadMaggie,
    Ash,
    Wraith,
    Pathfinder,
    Bangalore,
    Lifeline,
    Rampart,
    Sparrow,
    Mirage,
    Octane,
    Loba,
    Wattson,
    Alter,
    Caustic,
    Conduit,
    Fuse,
    Newcastle,
    Bloodhound,
    Ballistic,
    Vantage,
    Gibraltar,
    Seer,
    Crypto,
    Catalyst,
    None
}

public static class ApexLegendExtensions
{
    public static ApexLegend GetLegendByName(this string legendName)
    {
        var normalized = legendName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            "valkyrie" => ApexLegend.Valkyrie,
            "horizon" => ApexLegend.Horizon,
            "revenant" => ApexLegend.Revenant,
            "mad maggie" or "madmaggie" => ApexLegend.MadMaggie,
            "ash" => ApexLegend.Ash,
            "wraith" => ApexLegend.Wraith,
            "pathfinder" => ApexLegend.Pathfinder,
            "bangalore" => ApexLegend.Bangalore,
            "lifeline" => ApexLegend.Lifeline,
            "rampart" => ApexLegend.Rampart,
            "sparrow" => ApexLegend.Sparrow,
            "mirage" => ApexLegend.Mirage,
            "octane" => ApexLegend.Octane,
            "loba" => ApexLegend.Loba,
            "wattson" => ApexLegend.Wattson,
            "alter" => ApexLegend.Alter,
            "caustic" => ApexLegend.Caustic,
            "conduit" => ApexLegend.Conduit,
            "fuse" => ApexLegend.Fuse,
            "newcastle" => ApexLegend.Newcastle,
            "bloodhound" => ApexLegend.Bloodhound,
            "ballistic" => ApexLegend.Ballistic,
            "vantage" => ApexLegend.Vantage,
            "gibraltar" => ApexLegend.Gibraltar,
            "seer" => ApexLegend.Seer,
            "crypto" => ApexLegend.Crypto,
            "catalyst" => ApexLegend.Catalyst,
            _ => ApexLegend.None
        };
    }
}