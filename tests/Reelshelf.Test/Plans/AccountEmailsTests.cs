using Reelshelf.Billing;
using Reelshelf.Email;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class AccountEmailsTests
{
    private static readonly DateTimeOffset DeletesAt = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    private static AccountPlan Plan(AccountTier tier, int clipCount) =>
        new(tier, TierLimits.For(tier), clipCount, 0, 0);

    [Theory]
    [InlineData(AccountTier.Free, 95, true)]
    [InlineData(AccountTier.Plus, 950, true)]
    [InlineData(AccountTier.Pro, 4_750, false)]
    public void NearlyFull_OffersAnUpgradeOnlyBelowTheBestPlanToBuy(AccountTier tier, int clipCount, bool upgrade)
    {
        EmailMessage message = AccountEmails.ClipLimitNearlyReached("harry@example.com", Plan(tier, clipCount),
            "https://clips.example/");

        Assert.Equal(upgrade, message.Text.Contains("upgrade"));
        Assert.Equal(upgrade, message.Html.Contains("upgrade"));
        Assert.Contains("you'll need to delete some clips", message.Text);
    }

    [Fact]
    public void OverLimit_SaysPermanently_AndOffersAnUpgradeOnlyWhenOneKeepsThemAll()
    {
        EmailMessage free = AccountEmails.OverLimitStarted("harry@example.com", 40, DeletesAt, true,
            "https://clips.example/plan");
        EmailMessage pro = AccountEmails.OverLimitStarted("harry@example.com", 40, DeletesAt, false,
            "https://clips.example/plan");

        Assert.Equal("40 of your Reelshelf clips are over your plan's limit", free.Subject);
        Assert.Contains("permanently deleted on 17 November 2026", free.Text);
        Assert.Contains("or upgrade to keep them all", free.Text);
        Assert.DoesNotContain("upgrade", pro.Text);
        Assert.DoesNotContain("upgrade", pro.Html);
    }

    [Fact]
    public void OverLimitEndingSoon_NeverOffersDownloadingAsAWayToKeepThem()
    {
        EmailMessage message = AccountEmails.OverLimitEndingSoon("harry@example.com", 1, DeletesAt, false,
            "https://clips.example/plan");

        Assert.Equal("1 of your Reelshelf clips will be deleted on 17 November 2026", message.Subject);
        Assert.Contains("To keep them here, pick other clips to let go:", message.Text);
        Assert.DoesNotContain("upgrade", message.Text);
    }

    [Fact]
    public void PlanAtNoLimit_CanNeverBuyMoreClips()
    {
        Assert.False(Plan(AccountTier.Unlimited, 10_000).CanBuyMoreClips);
        Assert.True(Plan(AccountTier.Free, 0).CanBuyMoreClips);
    }
}
