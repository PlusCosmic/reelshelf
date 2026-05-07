using System.Text.Json.Serialization;

namespace Nucleus.Clips.ApexLegends.Models;

public record MapRotationResponse(
    [property: JsonPropertyName("battle_royale")]
    GameModeRotation BattleRoyale,
    [property: JsonPropertyName("ranked")] GameModeRotation Ranked
);

public record GameModeRotation(
    [property: JsonPropertyName("current")]
    MapRotationInfo Current,
    [property: JsonPropertyName("next")] MapRotationInfo Next
);

public record MapRotationInfo(
    [property: JsonPropertyName("start")] long Start,
    [property: JsonPropertyName("end")] long End,
    [property: JsonPropertyName("readableDate_start")]
    string ReadableDateStart,
    [property: JsonPropertyName("readableDate_end")]
    string ReadableDateEnd,
    [property: JsonPropertyName("map")] string Map,
    [property: JsonPropertyName("code")] string Code,
    int DurationInSecs,
    int DurationInMinutes,
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("remainingSecs")]
    int? RemainingSecs = null,
    [property: JsonPropertyName("remainingMins")]
    int? RemainingMins = null,
    [property: JsonPropertyName("remainingTimer")]
    string? RemainingTimer = null
)
{
    public DateTimeOffset StartTime => DateTimeOffset.FromUnixTimeSeconds(Start);
    public DateTimeOffset EndTime => DateTimeOffset.FromUnixTimeSeconds(End);
    public Uri AssetUri => new(Asset);
}