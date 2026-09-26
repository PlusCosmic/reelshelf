using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// The prompt, response schema and reference sheet embedded from <c>Resources/</c>. They are identical on every
/// request, so they are loaded once. <see cref="PromptVersion"/> is a hash of all three and the recognizer's
/// request layout, recorded on each run so results from different prompts are never compared as the same.
/// </summary>
public sealed class LegendDetectionResources
{
    public string Prompt { get; }
    public JsonElement ResponseSchema { get; }
    public byte[] ReferenceSheet { get; }
    public string PromptVersion { get; }

    public LegendDetectionResources()
    {
        byte[] prompt = Read("LegendDetection.prompt.md");
        byte[] schema = Read("LegendDetection.response-schema.json");
        ReferenceSheet = Read("LegendDetection.reference-sheet.png");

        Prompt = Encoding.UTF8.GetString(prompt).Trim();
        using (JsonDocument document = JsonDocument.Parse(schema))
        {
            ResponseSchema = document.RootElement.Clone();
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prompt);
        hash.AppendData(schema);
        hash.AppendData(ReferenceSheet);
        hash.AppendData(Encoding.UTF8.GetBytes(ChatClientLegendRecognizer.RequestLayoutVersion));
        PromptVersion = Convert.ToHexStringLower(hash.GetHashAndReset())[..12];
    }

    private static byte[] Read(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"Embedded resource {name} is missing");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>
/// The legends on <c>Resources/reference-sheet.png</c>, spelled as the sheet labels them. The model is told to
/// answer only with names from the sheet; anything else it returns is discarded rather than stored.
/// Add a legend here and to the sheet together.
/// </summary>
public static class ApexLegendNames
{
    public static readonly IReadOnlyList<string> All =
    [
        "Alter", "Ash", "Axle", "Ballistic", "Bangalore", "Bloodhound", "Catalyst", "Caustic", "Conduit", "Crypto",
        "Fuse", "Gibraltar", "Horizon", "Lifeline", "Loba", "Mad Maggie", "Mirage", "Newcastle", "Octane",
        "Pathfinder", "Rampart", "Revenant", "Seer", "Sparrow", "Valkyrie", "Vantage", "Wattson", "Wraith"
    ];

    /// <summary>The sheet's spelling of <paramref name="name"/>, or null when it is not a legend on the sheet.</summary>
    public static string? Canonicalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = Normalize(name);
        return All.FirstOrDefault(legend => Normalize(legend) == normalized);
    }

    private static string Normalize(string name)
    {
        return new string(name.Where(char.IsLetter).ToArray()).ToLowerInvariant();
    }
}
