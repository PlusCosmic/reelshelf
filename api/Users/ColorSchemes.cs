namespace Reelshelf.Users;

/// <summary>
/// The colour schemes an account can pick for the app (Settings → Appearance), each a set of the frontend's colour
/// tokens (<c>data-color-scheme</c> in <c>frontend/src/styles/reelshelf.css</c>). Stored in
/// <c>app_user.color_scheme</c>, whose check constraint lists the same names; null there is <see cref="Default"/>.
/// </summary>
public static class ColorSchemes
{
    public const string Stacks = "stacks";
    public const string Oxblood = "oxblood";
    public const string Ink = "ink";
    public const string Walnut = "walnut";
    public const string Graphite = "graphite";

    public const string Default = Stacks;

    public static readonly IReadOnlyList<string> All = [Stacks, Oxblood, Ink, Walnut, Graphite];

    /// <summary>The scheme named, ignoring case and surrounding space, or false for anything else.</summary>
    public static bool TryNormalize(string? value, out string scheme)
    {
        string candidate = value?.Trim().ToLowerInvariant() ?? "";
        scheme = All.FirstOrDefault(name => name == candidate) ?? Default;
        return All.Contains(candidate);
    }

    /// <summary>What a stored value means: a known scheme, else the default.</summary>
    public static string FromStored(string? stored) => TryNormalize(stored, out string scheme) ? scheme : Default;
}
