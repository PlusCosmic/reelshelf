using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Users;

public class UsernamesTests
{
    [Theory]
    [InlineData("Harry", "harry")]
    [InlineData("  harry_smith.99 ", "harry_smith.99")]
    [InlineData("hs", "hs")]
    public void Normalizes_ValidUsernames(string input, string expected)
    {
        Assert.True(Usernames.TryNormalize(input, out string username));
        Assert.Equal(expected, username);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("harry smith")]
    [InlineData("harry@example.com")]
    [InlineData("héllo")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void Refuses_InvalidUsernames(string? input)
    {
        Assert.False(Usernames.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData("Harry Smith", "harrysmith")]
    [InlineData("Zoë O'Brien", "zoeobrien")]
    [InlineData("_Harry_", "harry")]
    [InlineData("李", "player")]
    [InlineData(null, "player")]
    [InlineData("A very long display name indeed, longer than allowed", "averylongdisplaynameindeedlonger")]
    public void Suggest_MakesAValidUsernameFromTheName(string? displayName, string expected)
    {
        string suggestion = Usernames.Suggest(displayName);

        Assert.Equal(expected, suggestion);
        Assert.True(Usernames.TryNormalize(suggestion, out _));
    }
}
