using System.Text.RegularExpressions;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>
/// Every word the Windows app shows lives in <c>UploaderCopy</c>, as the copy guide asks. This reads the app's windows,
/// tray and XAML (which are built only on Windows) and fails on words written into them.
/// </summary>
public partial class AppCopyTests
{
    [Fact]
    public void TheAppsWindowsAndTray_WriteNoWordsOfTheirOwn()
    {
        string app = Path.Combine(RepoRoot(), "desktop", "Reelshelf.Uploader");
        List<string> found = [];
        foreach (string file in Directory.EnumerateFiles(Path.Combine(app, "Views"), "*.cs")
                     .Append(Path.Combine(app, "TrayController.cs")))
        {
            // Comments may quote the copy they describe.
            string code = CommentLine().Replace(File.ReadAllText(file), "");
            foreach (Match literal in StringLiteral().Matches(code))
            {
                string text = literal.Groups[1].Value;
                if (Words().IsMatch(text))
                {
                    found.Add($"{Path.GetFileName(file)}: \"{text}\"");
                }
            }
        }

        foreach (string file in Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match attribute in TextAttribute().Matches(File.ReadAllText(file)))
            {
                found.Add($"{Path.GetFileName(file)}: {attribute.Value}");
            }
        }

        Assert.Empty(found);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline)]
    private static partial Regex CommentLine();

    [GeneratedRegex("(?<![@$])\"((?:[^\"\\\\\\n]|\\\\.)*)\"")]
    private static partial Regex StringLiteral();

    /// <summary>Two words in a row, as people read them; class names, paths and grid sizes have none.</summary>
    [GeneratedRegex(@"[A-Za-z]{2,} [A-Za-z]{2,}")]
    private static partial Regex Words();

    [GeneratedRegex("(?:Text|Content|Title|Header|ToolTip\\.Tip|Watermark)=\"[^\"{]+\"")]
    private static partial Regex TextAttribute();
}
