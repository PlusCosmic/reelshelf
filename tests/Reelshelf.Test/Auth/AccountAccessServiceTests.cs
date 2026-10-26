using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.Exceptions;
using Xunit;

namespace Reelshelf.Test.Auth;

public class AccountAccessServiceTests
{
    private static readonly AuthenticatedUser Admin =
        new(Guid.NewGuid(), "admin", null, null, null, true, UserRole.Admin, null, []);

    [Fact]
    public async Task AnAdmin_CantChangeTheirOwnRole()
    {
        FakeStore store = new();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            new AccountAccessService(store).SetRole(Admin, Admin.Id, UserRole.Editor, "stepping down"));
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EveryChange_NeedsAReason(string? reason)
    {
        FakeStore store = new();
        AccountAccessService access = new(store);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            access.SetRole(Admin, Guid.NewGuid(), UserRole.Admin, reason));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            access.SetGrantedTier(Admin, Guid.NewGuid(), AccountTier.Pro, reason));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task ALongReason_IsRefused()
    {
        FakeStore store = new();

        await Assert.ThrowsAsync<BadRequestException>(() => new AccountAccessService(store)
            .SetGrantedTier(Admin, Guid.NewGuid(), AccountTier.Pro, new string('x', 501)));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Free_CantBeGranted()
    {
        FakeStore store = new();

        await Assert.ThrowsAsync<BadRequestException>(() => new AccountAccessService(store)
            .SetGrantedTier(Admin, Guid.NewGuid(), AccountTier.Free, "test"));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Changes_AreRecordedAsTheAdmin_WithTheReasonTrimmed()
    {
        FakeStore store = new();
        Guid account = Guid.NewGuid();
        AccountAccessService access = new(store);

        await access.SetRole(Admin, account, UserRole.Admin, " helps run the site ");
        await access.SetGrantedTier(Admin, account, null, "no longer testing");

        Assert.Equal(
        [
            $"role {account} Admin {Admin.Id} helps run the site",
            $"tier {account} none {Admin.Id} no longer testing"
        ], store.Calls);
    }

    [Fact]
    public async Task DemotingTheLastAdmin_IsAConflict()
    {
        FakeStore store = new() { Outcome = AccessChangeOutcome.LastAdmin };

        await Assert.ThrowsAsync<ConflictException>(() =>
            new AccountAccessService(store).SetRole(Admin, Guid.NewGuid(), UserRole.Editor, "test"));
    }

    [Fact]
    public async Task AnAccountThatIsGone_IsNotFound()
    {
        FakeStore store = new() { Outcome = AccessChangeOutcome.NotFound };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new AccountAccessService(store).SetGrantedTier(Admin, Guid.NewGuid(), AccountTier.Plus, "test"));
    }

    [Theory]
    [InlineData("plus", AccountTier.Plus)]
    [InlineData("Pro", AccountTier.Pro)]
    [InlineData("UNLIMITED", AccountTier.Unlimited)]
    public void GrantableTiers_ParseByName(string name, AccountTier expected)
    {
        Assert.Equal(expected, GrantedTiers.Parse(name));
    }

    [Theory]
    [InlineData("free")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherTierNames_AreNoGrant(string? name)
    {
        Assert.Null(GrantedTiers.Parse(name));
    }

    private sealed class FakeStore : IAccountAccessStore
    {
        public AccessChangeOutcome Outcome { get; init; } = AccessChangeOutcome.Changed;

        public List<string> Calls { get; } = [];

        public Task<AccessChangeOutcome> SetRole(Guid userId, UserRole role, Guid changedBy, string reason)
        {
            Calls.Add($"role {userId} {role} {changedBy} {reason}");
            return Task.FromResult(Outcome);
        }

        public Task<AccessChangeOutcome> SetGrantedTier(Guid userId, AccountTier? tier, Guid changedBy,
            string reason)
        {
            Calls.Add($"tier {userId} {tier?.ToString() ?? "none"} {changedBy} {reason}");
            return Task.FromResult(Outcome);
        }

        public Task<AccountTier?> GetGrantedTier(Guid userId) => throw new NotSupportedException();

        public Task<List<Guid>> GetUserIdsWithGrantedTierAtLeast(AccountTier minimum) =>
            throw new NotSupportedException();

        public Task<List<AccountAccessRow>> FindAccounts(string query, int limit) => throw new NotSupportedException();

        public Task<AccountAccessRow?> GetAccount(Guid userId) => throw new NotSupportedException();

        public Task<List<AccountAccessChange>> GetChanges(Guid userId) => throw new NotSupportedException();
    }
}
