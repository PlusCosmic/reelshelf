using Reelshelf.Billing;
using Xunit;

namespace Reelshelf.Test.Billing;

public class AccountTiersTests
{
    private static BillingSubscription Subscription(string status, AccountTier? tier) =>
        new($"sub_{Guid.NewGuid():N}", Guid.NewGuid(), status, null, tier, null, false, null, null);

    [Fact]
    public void Whitelisted_IsUnlimited_WhateverTheSubscriptions()
    {
        Assert.Equal(AccountTier.Unlimited, AccountTiers.Resolve(true, [Subscription("active", AccountTier.Plus)]));
    }

    [Fact]
    public void NoSubscription_IsFree()
    {
        Assert.Equal(AccountTier.Free, AccountTiers.Resolve(false, []));
    }

    [Theory]
    [InlineData("active", AccountTier.Pro)]
    [InlineData("trialing", AccountTier.Pro)]
    [InlineData("past_due", AccountTier.Pro)]
    [InlineData("unpaid", AccountTier.Free)]
    [InlineData("paused", AccountTier.Free)]
    [InlineData("canceled", AccountTier.Free)]
    [InlineData("incomplete", AccountTier.Free)]
    [InlineData("incomplete_expired", AccountTier.Free)]
    public void OnlyActiveTrialingAndPastDueGrantTheirTier(string status, AccountTier expected)
    {
        Assert.Equal(expected, AccountTiers.Resolve(false, [Subscription(status, AccountTier.Pro)]));
    }

    [Fact]
    public void TheBestLiveSubscriptionWins()
    {
        AccountTier tier = AccountTiers.Resolve(false,
        [
            Subscription("active", AccountTier.Plus),
            Subscription("canceled", AccountTier.Pro),
            Subscription("past_due", AccountTier.Pro)
        ]);

        Assert.Equal(AccountTier.Pro, tier);
    }

    [Fact]
    public void ASubscriptionOnAnUnknownPlan_GrantsNothing()
    {
        Assert.Equal(AccountTier.Free, AccountTiers.Resolve(false, [Subscription("active", null)]));
    }

    [Theory]
    [InlineData("plus_monthly", AccountTier.Plus)]
    [InlineData("plus_yearly", AccountTier.Plus)]
    [InlineData("pro_monthly", AccountTier.Pro)]
    [InlineData("pro_yearly", AccountTier.Pro)]
    public void Plans_AreFoundByLookupKey(string lookupKey, AccountTier tier)
    {
        Assert.Equal(tier, BillingPlans.Find(lookupKey)?.Tier);
    }

    [Theory]
    [InlineData("PRO_MONTHLY")]
    [InlineData("free")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownLookupKeys_AreNoPlan(string? lookupKey)
    {
        Assert.Null(BillingPlans.Find(lookupKey));
    }
}
