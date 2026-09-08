using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Users;

public class EmailAddressTests
{
    [Theory]
    [InlineData("Harry@Example.com", "harry@example.com")]
    [InlineData("  harry+clips@example.co.uk ", "harry+clips@example.co.uk")]
    public void Normalizes_ValidAddresses(string input, string expected)
    {
        Assert.True(EmailAddress.TryNormalize(input, out string? email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInput_IsAllowedAndClearsTheAddress(string? input)
    {
        Assert.True(EmailAddress.TryNormalize(input, out string? email));
        Assert.Null(email);
    }

    [Theory]
    [InlineData("harry")]
    [InlineData("harry@localhost")]
    [InlineData("Harry <harry@example.com>")]
    [InlineData("harry@example.com, other@example.com")]
    public void Rejects_AnythingButOnePlainMailbox(string input)
    {
        Assert.False(EmailAddress.TryNormalize(input, out string? email));
        Assert.Null(email);
    }
}
