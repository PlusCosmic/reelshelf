using Reelshelf.Billing;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class AccountPlanResponseTests
{
    private static readonly AccountPlan Plus = new(AccountTier.Plus, TierLimits.For(AccountTier.Plus), 40, 5, 0);

    [Fact]
    public void WithPaidPlansOfferedASubscriberCanBuyChangeAndManage()
    {
        AccountPlanResponse response = AccountPlanResponse.From(Plus, [], billingEnabled: true,
            paidPlansOffered: true, hasCustomer: true, canChangePlan: true);

        Assert.True(response.BillingEnabled);
        Assert.True(response.CanChangePlan);
        Assert.True(response.CanManageBilling);
    }

    [Fact]
    public void WithThePaidPlansFlagOffASubscriberKeepsTheCustomerPortalOnly()
    {
        AccountPlanResponse response = AccountPlanResponse.From(Plus, [], billingEnabled: true,
            paidPlansOffered: false, hasCustomer: true, canChangePlan: true);

        Assert.False(response.BillingEnabled);
        Assert.False(response.CanChangePlan);
        Assert.True(response.CanManageBilling);
    }

    [Fact]
    public void WithBillingOffNothingIsOffered()
    {
        AccountPlanResponse response = AccountPlanResponse.From(Plus, [], billingEnabled: false,
            paidPlansOffered: true, hasCustomer: true, canChangePlan: true);

        Assert.False(response.BillingEnabled);
        Assert.False(response.CanChangePlan);
        Assert.False(response.CanManageBilling);
    }
}
