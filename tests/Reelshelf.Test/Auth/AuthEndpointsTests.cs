using Microsoft.AspNetCore.Authentication;
using Reelshelf.Auth;
using Xunit;

namespace Reelshelf.Test.Auth;

public class AuthEndpointsTests
{
    [Fact]
    public void SessionsArePersistent_AndExpireByTheCookieScheme()
    {
        AuthenticationProperties properties = AuthEndpoints.SessionProperties();

        Assert.True(properties.IsPersistent);
        Assert.Null(properties.ExpiresUtc);
    }
}
