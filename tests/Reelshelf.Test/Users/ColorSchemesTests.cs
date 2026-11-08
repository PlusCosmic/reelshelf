using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Users;

public class ColorSchemesTests
{
    [Theory]
    [InlineData("stacks", "stacks")]
    [InlineData("Oxblood", "oxblood")]
    [InlineData(" ink ", "ink")]
    [InlineData("WALNUT", "walnut")]
    [InlineData("graphite", "graphite")]
    public void Normalizes_KnownSchemes(string input, string expected)
    {
        Assert.True(ColorSchemes.TryNormalize(input, out string scheme));
        Assert.Equal(expected, scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("green")]
    [InlineData("stacks; drop table")]
    public void Refuses_UnknownSchemes(string? input)
    {
        Assert.False(ColorSchemes.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData(null, "stacks")]
    [InlineData("ink", "ink")]
    [InlineData("retired", "stacks")]
    public void Reads_StoredValues(string? stored, string expected)
    {
        Assert.Equal(expected, ColorSchemes.FromStored(stored));
    }

    [Fact]
    public void Migration_AllowsEveryScheme()
    {
        string migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "migrations", "db", "migrations",
            "V65__app_user_color_scheme.sql"));
        foreach (string scheme in ColorSchemes.All)
        {
            Assert.Contains($"'{scheme}'", migration);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Reelshelf.sln not found");
    }
}
