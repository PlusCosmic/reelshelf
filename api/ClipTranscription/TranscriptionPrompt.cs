using System.Security.Cryptography;
using System.Text;
using Reelshelf.ApexLegends.LegendDetection;

namespace Reelshelf.ClipTranscription;

/// <summary>
/// The context sent with a clip's audio: a short prompt naming the game, and keywords the model should spell as
/// given, for models that take keywords. <see cref="Version"/> hashes the template, keyword rules and request layout,
/// so runs with different requests are never compared as the same; the prompt and keywords actually sent are stored
/// on each run.
/// </summary>
public sealed record TranscriptionPrompt(string Prompt, IReadOnlyList<string> Keywords)
{
    private const string ApexLegendsSlug = "apex-legends";

    private const string Template =
        "A gameplay clip from {0}. The players may be talking to each other over voice chat, reacting to the " +
        "game, or not speaking at all. Transcribe only what is said.";

    private const string UnknownGame = "a video game";

    public static readonly string Version = ComputeVersion();

    public static TranscriptionPrompt For(string? gameName, string? gameSlug, string model)
    {
        string game = Clean(gameName);
        string prompt = string.Format(Template, game.Length == 0 ? UnknownGame : game);

        List<string> keywords = [];
        if (!OpenAIClipTranscriber.SupportsKeywords(model))
        {
            return new TranscriptionPrompt(prompt, keywords);
        }

        if (game.Length > 0)
        {
            keywords.Add(game);
        }

        if (gameSlug == ApexLegendsSlug)
        {
            keywords.AddRange(ApexLegendNames.All);
        }

        return new TranscriptionPrompt(prompt, keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Keywords may not contain angle brackets or line breaks, so a game name loses them.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        string[] words = value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word => word.Replace("<", "").Replace(">", ""))
            .Where(word => word.Length > 0));
    }

    private static string ComputeVersion()
    {
        string source = string.Join('\n', Template, UnknownGame, ApexLegendsSlug, string.Join(',', ApexLegendNames.All),
            OpenAIClipTranscriber.RequestLayoutVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
    }
}
