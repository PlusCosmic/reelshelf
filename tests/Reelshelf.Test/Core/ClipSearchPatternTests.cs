using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipSearchPatternTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSearch_IsNull(string? search)
    {
        Assert.Null(ClipsStatements.ToContainsPattern(search));
    }

    [Fact]
    public void PlainSearch_BecomesAContainsPattern()
    {
        Assert.Equal("%ranked%", ClipsStatements.ToContainsPattern("ranked"));
    }

    [Theory]
    // A user typing a LIKE metacharacter means it literally; unescaped, "_" alone matches every clip.
    [InlineData("_", @"%\_%")]
    [InlineData("100%", @"%100\%%")]
    [InlineData("clip_01", @"%clip\_01%")]
    // The escape character itself has to be escaped, or it swallows the character after it.
    [InlineData(@"a\b", @"%a\\b%")]
    [InlineData(@"\_", @"%\\\_%")]
    public void LikeMetacharacters_AreEscaped(string search, string expected)
    {
        Assert.Equal(expected, ClipsStatements.ToContainsPattern(search));
    }
}
