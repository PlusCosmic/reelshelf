using Reelshelf.Billing;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class TierLimitsTests
{
    [Fact]
    public void Tiers_HaveTheAdrsLimits()
    {
        Assert.Equal(new TierLimits(100, 60, 200), TierLimits.For(AccountTier.Free));
        Assert.Equal(new TierLimits(1_000, 120, 2_000), TierLimits.For(AccountTier.Plus));
        Assert.Equal(new TierLimits(5_000, 120, 10_000), TierLimits.For(AccountTier.Pro));
        Assert.Equal(new TierLimits(null, null, null), TierLimits.For(AccountTier.Unlimited));
    }

    [Theory]
    [InlineData(AccountTier.Free, false)]
    [InlineData(AccountTier.Plus, false)]
    [InlineData(AccountTier.Pro, true)]
    [InlineData(AccountTier.Unlimited, true)]
    public void OnlyProAndUnlimited_HaveTranscripts(AccountTier tier, bool expected)
    {
        Assert.Equal(expected, TierLimits.HasTranscripts(tier));
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(60.99)]
    [InlineData(61.0)]
    public void Free_KeepsClipsUpToAMinute_WithASecondForRounding(double seconds)
    {
        Assert.Null(TierLimits.For(AccountTier.Free).LengthProblem(seconds));
    }

    [Fact]
    public void Free_RefusesALongerClip_SayingHowLongEachIs()
    {
        string? problem = TierLimits.For(AccountTier.Free).LengthProblem(95.4);

        Assert.Equal("This clip is 95 s long, and your plan keeps clips of up to 1 min. Trim it, or upgrade for longer clips.",
            problem);
    }

    [Fact]
    public void Paid_KeepsTwoMinutes()
    {
        TierLimits plus = TierLimits.For(AccountTier.Plus);

        Assert.Null(plus.LengthProblem(120.5));
        Assert.Contains("up to 2 min", plus.LengthProblem(150));
    }

    [Fact]
    public void Unlimited_AndAnUnknownLength_AreNeverRefused()
    {
        Assert.Null(TierLimits.For(AccountTier.Unlimited).LengthProblem(3_600));
        Assert.Null(TierLimits.For(AccountTier.Free).LengthProblem(null));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(99, 199, null)]
    [InlineData(100, 0, "Your plan keeps up to 100 clips and you have 100.")]
    [InlineData(150, 0, "Your plan keeps up to 100 clips and you have 150.")]
    [InlineData(10, 200, "You've added 200 clips in the last 30 days")]
    public void Free_RefusesANewClipAtEitherLimit(int clipCount, int newClips, string? expected)
    {
        AccountPlan plan = new(AccountTier.Free, TierLimits.For(AccountTier.Free), clipCount, newClips, 0);

        string? problem = plan.NewClipProblem();

        if (expected is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.StartsWith(expected, problem);
        }
    }

    [Fact]
    public void Unlimited_NeverRefusesANewClip()
    {
        AccountPlan plan = new(AccountTier.Unlimited, TierLimits.For(AccountTier.Unlimited), 1_000_000, 1_000_000, 0);

        Assert.Null(plan.NewClipProblem());
    }
}
