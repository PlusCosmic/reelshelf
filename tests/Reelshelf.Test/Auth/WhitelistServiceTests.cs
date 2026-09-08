using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Auth;
using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Auth;

public class WhitelistServiceTests
{
    private static WhitelistService Load(string json)
    {
        return new WhitelistService(WhitelistService.Parse(json, NullLogger.Instance));
    }

    [Fact]
    public void MatchesDiscordAndTwitchIds()
    {
        WhitelistService whitelist = Load("""
            { "Users": [
                { "DiscordId": "1001", "Role": "Admin" },
                { "TwitchId": "2002" }
            ] }
            """);

        Assert.Equal(UserRole.Admin, whitelist.GetRole([new UserIdentityRef(AuthProvider.Discord, "1001")]));
        Assert.True(whitelist.IsWhitelisted([new UserIdentityRef(AuthProvider.Twitch, "2002")]));
        Assert.Null(whitelist.GetRole([new UserIdentityRef(AuthProvider.Twitch, "2002")]));
        Assert.False(whitelist.IsWhitelisted([new UserIdentityRef(AuthProvider.Twitch, "1001")]));
    }

    [Fact]
    public void AnyLinkedIdentityAppliesTheEntry_AndRoleOverrideWins()
    {
        WhitelistService whitelist = Load("""
            { "Users": [
                { "DiscordId": "1001" },
                { "TwitchId": "2002", "Role": "Admin" }
            ] }
            """);

        UserIdentityRef[] identities =
        [
            new(AuthProvider.Discord, "1001"),
            new(AuthProvider.Twitch, "2002")
        ];

        Assert.True(whitelist.IsWhitelisted(identities));
        Assert.Equal(UserRole.Admin, whitelist.GetRole(identities));
    }

    [Fact]
    public void OneEntryCanNameBothProviders()
    {
        WhitelistService whitelist = Load("""
            { "Users": [ { "DiscordId": "1001", "TwitchId": "2002", "Role": "Editor" } ] }
            """);

        Assert.Equal(2, whitelist.Count);
        Assert.Equal(UserRole.Editor, whitelist.GetRole([new UserIdentityRef(AuthProvider.Twitch, "2002")]));
    }

    [Fact]
    public void LegacyFormat_GrantsTierWithoutRole()
    {
        WhitelistService whitelist = Load("""{ "WhitelistedDiscordUserIds": ["1001"] }""");

        Assert.True(whitelist.IsWhitelisted([new UserIdentityRef(AuthProvider.Discord, "1001")]));
        Assert.Null(whitelist.GetRole([new UserIdentityRef(AuthProvider.Discord, "1001")]));
    }
}
