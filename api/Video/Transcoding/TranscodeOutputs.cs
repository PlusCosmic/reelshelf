using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reelshelf.Video.Transcoding;

/// <summary>
/// What the clip needs from a completion's <c>outputs</c> (<c>{"codecs": "...", "width": 1920, "height": 1080,
/// "files": [...]}</c>): the codecs and height for its playback source. The rest stays on the run as reported. A value
/// that is missing or doesn't look right is left null rather than failing the report: the clip still plays as plain
/// <c>video/mp4</c>.
/// </summary>
public sealed partial record TranscodeOutputs(string? Codecs, int? Height)
{
    public const int MaxCodecsLength = 128;
    public const int MaxHeight = 8640;

    public static TranscodeOutputs From(JsonElement? outputs)
    {
        if (outputs is not { ValueKind: JsonValueKind.Object } value)
        {
            return new TranscodeOutputs(null, null);
        }

        string? codecs = value.TryGetProperty("codecs", out JsonElement codecsValue) &&
                         codecsValue.ValueKind == JsonValueKind.String
            ? NormalizeCodecs(codecsValue.GetString())
            : null;
        int? height = value.TryGetProperty("height", out JsonElement heightValue) &&
                      heightValue.ValueKind == JsonValueKind.Number &&
                      heightValue.TryGetInt32(out int parsed) && parsed is > 0 and <= MaxHeight
            ? parsed
            : null;
        return new TranscodeOutputs(codecs, height);
    }

    /// <summary>
    /// An RFC 6381 codecs list such as <c>av01.0.08M.08,opus</c>, without spaces. It goes inside the quotes of a
    /// MIME type parameter, so anything beyond letters, digits and <c>. + -</c> between the commas is refused.
    /// </summary>
    public static string? NormalizeCodecs(string? codecs)
    {
        if (codecs is null)
        {
            return null;
        }

        string normalized = string.Join(',', codecs.Split(',').Select(codec => codec.Trim()));
        return normalized.Length <= MaxCodecsLength && CodecsPattern().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9.+\-]+(,[A-Za-z0-9.+\-]+)*$")]
    private static partial Regex CodecsPattern();
}
