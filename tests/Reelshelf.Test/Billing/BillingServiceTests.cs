using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Xunit;

namespace Reelshelf.Test.Billing;

public class BillingServiceTests
{
    private static readonly AuthenticatedUser User = new(Guid.NewGuid(), "harry", null, null, "harry@example.com", true,
        UserRole.Editor, null, []);

    private static (BillingService Service, FakeStripe Stripe, FakeStore Store) Build() => Build(new NoPlanEndingNotifier());

    private static (BillingService Service, FakeStripe Stripe, FakeStore Store) Build(IPlanEndingNotifier planEnding,
        string planChangeConfiguration = "bpc_change")
    {
        FakeStripe stripe = new();
        FakeStore store = new();
        store.Users.Add(User.Id);
        BillingOptions options = new()
        {
            SecretKey = "rk_test",
            WebhookSecret = "whsec_test",
            FrontendOrigin = "https://reelshelf.test",
            PlanChangePortalConfiguration = planChangeConfiguration
        };
        BillingService service = new(stripe, store, options, new MemoryCache(new MemoryCacheOptions()), planEnding,
            NullLogger<BillingService>.Instance);
        return (service, stripe, store);
    }

    private static StripeSubscriptionState State(string id, string customerId, string status = "active",
        string? lookupKey = "plus_monthly", Guid? userId = null) =>
        new(id, customerId, status, lookupKey, DateTimeOffset.Parse("2026-11-23T00:00:00Z"), false, null, null, userId);

    [Fact]
    public async Task StartCheckout_ForANewCustomer_SendsTheirEmailAndTheReturnPages()
    {
        var (service, stripe, _) = Build();

        CheckoutResult result = await service.StartCheckout(User, "pro_yearly", CancellationToken.None);

        Assert.Equal(new CheckoutResult(CheckoutOutcome.Created, "https://checkout.stripe.test/1"), result);
        CheckoutSessionRequest request = Assert.Single(stripe.Checkouts);
        Assert.Equal("pro_yearly", request.Plan.LookupKey);
        Assert.Equal(User.Id, request.UserId);
        Assert.Null(request.CustomerId);
        Assert.Equal("harry@example.com", request.CustomerEmail);
        Assert.Equal("https://reelshelf.test/settings?billing=success", request.SuccessUrl);
        Assert.Equal("https://reelshelf.test/settings?billing=cancelled", request.CancelUrl);
    }

    [Fact]
    public async Task StartCheckout_ReusesTheAccountsCustomer()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";

        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal("cus_1", Assert.Single(stripe.Checkouts).CustomerId);
    }

    [Fact]
    public async Task StartCheckout_RefusesAnUnknownPlan()
    {
        var (service, stripe, _) = Build();

        CheckoutResult result = await service.StartCheckout(User, "free", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.UnknownPlan, result.Outcome);
        Assert.Empty(stripe.Checkouts);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("paused")]
    public async Task StartCheckout_WithAnOpenSubscription_SendsThemToThePortalInstead(string status)
    {
        var (service, stripe, store) = Build();
        store.Subscriptions.Add(new BillingSubscription("sub_1", User.Id, status, "plus_monthly", AccountTier.Plus, null,
            false, null, null));

        CheckoutResult result = await service.StartCheckout(User, "pro_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.AlreadySubscribed, result.Outcome);
        Assert.Empty(stripe.Checkouts);
    }

    [Fact]
    public async Task StartCheckout_RemembersTheSessionUnderTheAccountsLock()
    {
        var (service, _, store) = Build();

        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal("cs_1", store.CheckoutSessions[User.Id]);
        Assert.Equal(1, store.CheckoutLocks);
    }

    [Fact]
    public async Task StartCheckout_AfterAPaymentTheWebhookHasntBroughtYet_RecordsItAndRefusesASecond()
    {
        var (service, stripe, store) = Build();
        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);
        stripe.Sessions["cs_1"] = stripe.Sessions["cs_1"] with { Status = "complete", SubscriptionId = "sub_1" };
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", userId: User.Id);

        CheckoutResult result = await service.StartCheckout(User, "pro_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.AlreadySubscribed, result.Outcome);
        Assert.Single(stripe.Checkouts);
        Assert.Equal("active", Assert.Single(store.Subscriptions).Status);
        Assert.Equal("cus_1", store.Customers[User.Id]);
    }

    [Fact]
    public async Task StartCheckout_WhileAnotherIsOpen_ExpiresItSoOnlyTheNewOneCanBePaid()
    {
        var (service, stripe, _) = Build();
        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        CheckoutResult result = await service.StartCheckout(User, "pro_yearly", CancellationToken.None);

        Assert.Equal(new CheckoutResult(CheckoutOutcome.Created, "https://checkout.stripe.test/2"), result);
        Assert.Equal(["cs_1"], stripe.Expired);
    }

    [Fact]
    public async Task StartCheckout_WhenTheOpenOneIsPaidAsItIsExpired_RefusesASecond()
    {
        var (service, stripe, _) = Build();
        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", userId: User.Id);
        stripe.CompleteOnExpire = id => stripe.Sessions[id] with { Status = "complete", SubscriptionId = "sub_1" };

        CheckoutResult result = await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.AlreadySubscribed, result.Outcome);
        Assert.Single(stripe.Checkouts);
    }

    [Fact]
    public async Task StartCheckout_AfterACompletedCheckoutWhosePaymentFailed_IsAllowed()
    {
        var (service, stripe, _) = Build();
        await service.StartCheckout(User, "plus_monthly", CancellationToken.None);
        stripe.Sessions["cs_1"] = stripe.Sessions["cs_1"] with { Status = "complete", SubscriptionId = "sub_1" };
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", status: "incomplete", userId: User.Id);

        CheckoutResult result = await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Created, result.Outcome);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("incomplete")]
    [InlineData("incomplete_expired")]
    public async Task StartCheckout_AfterASubscriptionEndedOrNeverStarted_IsAllowed(string status)
    {
        var (service, _, store) = Build();
        store.Subscriptions.Add(new BillingSubscription("sub_1", User.Id, status, "plus_monthly", AccountTier.Plus, null,
            false, null, null));

        CheckoutResult result = await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task StartCheckout_WhenNoPriceHasTheLookupKey_IsUnavailable()
    {
        var (service, stripe, _) = Build();
        stripe.MissingPrices = true;

        CheckoutResult result = await service.StartCheckout(User, "plus_monthly", CancellationToken.None);

        Assert.Equal(CheckoutOutcome.PlanUnavailable, result.Outcome);
    }

    [Fact]
    public async Task OpenPortal_WithoutACustomer_IsNull()
    {
        var (service, _, _) = Build();

        Assert.Null(await service.OpenPortal(User, CancellationToken.None));
    }

    [Fact]
    public async Task OpenPortal_ReturnsToSettings()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";

        string? url = await service.OpenPortal(User, CancellationToken.None);

        Assert.Equal("https://portal.stripe.test/cus_1", url);
        Assert.Equal("https://reelshelf.test/settings?billing=portal", stripe.PortalReturnUrl);
    }

    [Fact]
    public async Task CheckoutCompleted_LinksTheCustomerAndRecordsTheSubscription()
    {
        var (service, stripe, store) = Build();
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", lookupKey: "pro_monthly");

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "checkout.session.completed", "sub_1", "cus_1", User.Id), CancellationToken.None);

        Assert.Equal("cus_1", store.Customers[User.Id]);
        BillingSubscription recorded = Assert.Single(store.Subscriptions);
        Assert.Equal(new BillingSubscription("sub_1", User.Id, "active", "pro_monthly", AccountTier.Pro,
            DateTimeOffset.Parse("2026-11-23T00:00:00Z"), false, null, null), recorded);
    }

    [Fact]
    public async Task ASubscriptionEventBeforeCheckoutCompleted_FindsTheAccountFromItsMetadata()
    {
        var (service, stripe, store) = Build();
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", userId: User.Id);

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "customer.subscription.created", "sub_1", "cus_1", null), CancellationToken.None);

        Assert.Equal("cus_1", store.Customers[User.Id]);
        Assert.Equal(User.Id, Assert.Single(store.Subscriptions).UserId);
    }

    [Fact]
    public async Task EveryEvent_RecordsStripesCurrentState_NotTheEvents()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", status: "canceled");

        // An older "created" event delivered late still ends in the subscription's current state.
        await service.HandleWebhook(
            new BillingWebhookEvent("evt_old", "customer.subscription.created", "sub_1", "cus_1", null), CancellationToken.None);

        Assert.Equal("canceled", Assert.Single(store.Subscriptions).Status);
    }

    [Fact]
    public async Task ARepeatedEvent_LeavesOneRow()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1");
        BillingWebhookEvent stripeEvent = new("evt_1", "invoice.paid", "sub_1", "cus_1", null);

        await service.HandleWebhook(stripeEvent, CancellationToken.None);
        await service.HandleWebhook(stripeEvent, CancellationToken.None);

        Assert.Single(store.Subscriptions);
    }

    [Fact]
    public async Task ASubscriptionNoAccountOwns_IsNotRecorded()
    {
        var (service, stripe, store) = Build();
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_unknown");

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "customer.subscription.updated", "sub_1", "cus_unknown", null), CancellationToken.None);

        Assert.Empty(store.Subscriptions);
        // It may be one made by hand in the Dashboard, so it isn't ours to cancel.
        Assert.Empty(stripe.Cancelled);
    }

    [Fact]
    public async Task ACheckoutForAnAccountThatWasDeleted_IsCancelled()
    {
        var (service, stripe, store) = Build();
        Guid deleted = Guid.NewGuid();
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", userId: deleted);

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "checkout.session.completed", "sub_1", "cus_1", deleted), CancellationToken.None);

        Assert.Equal(["sub_1"], stripe.Cancelled);
        Assert.Empty(store.Subscriptions);
    }

    [Fact]
    public async Task ASubscriptionOfAnAccountBeingDeleted_IsCancelled()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";
        store.Users.Remove(User.Id);
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1");

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "customer.subscription.updated", "sub_1", "cus_1", null), CancellationToken.None);

        Assert.Equal(["sub_1"], stripe.Cancelled);
    }

    [Fact]
    public async Task ACancelledSubscriptionOfAGoneAccount_IsLeftAlone()
    {
        var (service, stripe, _) = Build();
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", status: "canceled", userId: Guid.NewGuid());

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "customer.subscription.deleted", "sub_1", "cus_1", null), CancellationToken.None);

        Assert.Empty(stripe.Cancelled);
    }

    [Fact]
    public async Task ASubscriptionOnAnUnknownPrice_IsRecordedWithoutATier()
    {
        var (service, stripe, store) = Build();
        store.Customers[User.Id] = "cus_1";
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1", lookupKey: "legacy_plan");

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "customer.subscription.updated", "sub_1", "cus_1", null), CancellationToken.None);

        BillingSubscription recorded = Assert.Single(store.Subscriptions);
        Assert.Null(recorded.Tier);
    }

    [Fact]
    public async Task EventsBillingDoesntHandle_ReadNothing()
    {
        var (service, stripe, store) = Build();

        await service.HandleWebhook(
            new BillingWebhookEvent("evt_1", "charge.refunded", "sub_1", "cus_1", null), CancellationToken.None);

        Assert.Equal(0, stripe.SubscriptionReads);
        Assert.Empty(store.Subscriptions);
    }

    [Fact]
    public async Task ASubscriptionNewlySetToEnd_TellsTheOwnerOnce()
    {
        RecordingPlanEnding planEnding = new();
        var (service, stripe, store) = Build(planEnding);
        store.Customers[User.Id] = "cus_1";
        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1");
        BillingWebhookEvent updated = new("evt_1", "customer.subscription.updated", "sub_1", "cus_1", null);
        await service.HandleWebhook(updated, CancellationToken.None);
        Assert.Empty(planEnding.Ends);

        stripe.Subscriptions["sub_1"] = State("sub_1", "cus_1") with { CancelAtPeriodEnd = true };
        await service.HandleWebhook(updated, CancellationToken.None);
        await service.HandleWebhook(updated, CancellationToken.None);

        (Guid userId, DateTimeOffset endsAt) = Assert.Single(planEnding.Ends);
        Assert.Equal(User.Id, userId);
        Assert.Equal(DateTimeOffset.Parse("2026-11-23T00:00:00Z"), endsAt);
    }

    [Fact]
    public async Task Prices_AreReadFromStripeOnceAndKeyedByLookupKey()
    {
        var (service, stripe, _) = Build();
        stripe.Prices.Add(new StripePlanPrice("plus_monthly", 300, "gbp"));
        stripe.Prices.Add(new StripePlanPrice("pro_yearly", 8_000, "gbp"));

        IReadOnlyDictionary<string, StripePlanPrice> first = await service.GetPrices(CancellationToken.None);
        IReadOnlyDictionary<string, StripePlanPrice> second = await service.GetPrices(CancellationToken.None);

        Assert.Equal(300, first["plus_monthly"].UnitAmount);
        Assert.False(first.ContainsKey("plus_yearly"));
        Assert.Same(first, second);
        Assert.Equal(1, stripe.PriceReads);
    }

    [Fact]
    public async Task CancelAll_CancelsOnlySubscriptionsThatCouldStillBill()
    {
        var (service, stripe, store) = Build();
        foreach (string status in new[] { "active", "past_due", "unpaid", "incomplete", "canceled", "incomplete_expired" })
        {
            store.Subscriptions.Add(new BillingSubscription($"sub_{status}", User.Id, status, "plus_monthly",
                AccountTier.Plus, null, false, null, null));
        }

        await service.CancelAllAsync(User.Id, CancellationToken.None);

        Assert.Equal(["sub_active", "sub_past_due", "sub_unpaid", "sub_incomplete"], stripe.Cancelled);
    }

    private sealed class RecordingPlanEnding : IPlanEndingNotifier
    {
        public List<(Guid UserId, DateTimeOffset EndsAt)> Ends { get; } = [];

        public Task SubscriptionEndingAsync(Guid userId, BillingSubscription subscription, DateTimeOffset endsAt,
            CancellationToken cancellationToken)
        {
            Ends.Add((userId, endsAt));
            return Task.CompletedTask;
        }
    }

    /// <summary>An account on <paramref name="lookupKey"/>, recorded locally and at Stripe, with every plan's price.</summary>
    private static (BillingService Service, FakeStripe Stripe, FakeStore Store) Subscribed(string lookupKey,
        string planChangeConfiguration = "bpc_change")
    {
        var (service, stripe, store) = Build(new NoPlanEndingNotifier(), planChangeConfiguration);
        foreach (BillingPlan plan in BillingPlans.All)
        {
            stripe.Prices.Add(new StripePlanPrice(plan.LookupKey, 100, "gbp", $"price_{plan.LookupKey}"));
        }

        store.Customers[User.Id] = "cus_1";
        StripeSubscriptionState state = State("sub_1", "cus_1", lookupKey: lookupKey, userId: User.Id) with
        {
            ItemId = "si_1"
        };
        stripe.Subscriptions["sub_1"] = state;
        store.Subscriptions.Add(new BillingSubscription("sub_1", User.Id, "active", lookupKey,
            BillingPlans.Find(lookupKey)!.Tier, state.CurrentPeriodEnd, false, null, null));
        return (service, stripe, store);
    }

    [Theory]
    [InlineData("plus_monthly", "pro_monthly", true)]
    [InlineData("plus_yearly", "pro_monthly", true)]
    [InlineData("plus_monthly", "plus_yearly", true)]
    [InlineData("pro_monthly", "plus_yearly", false)]
    [InlineData("pro_yearly", "pro_monthly", false)]
    [InlineData("pro_yearly", "plus_monthly", false)]
    public void IsUpgrade_IsAHigherTierOrYearlyInsteadOfMonthly(string from, string to, bool upgrade)
    {
        Assert.Equal(upgrade, BillingService.IsUpgrade(BillingPlans.Find(from)!, BillingPlans.Find(to)!));
    }

    [Fact]
    public async Task ChangePlan_ToABetterPlan_OpensStripesConfirmationForIt()
    {
        var (service, stripe, _) = Subscribed("plus_monthly");

        PlanChangeResult result = await service.ChangePlan(User, "pro_yearly", CancellationToken.None);

        Assert.Equal(new PlanChangeResult(PlanChangeOutcome.Confirm, "https://portal.stripe.test/confirm/1"), result);
        Assert.Equal(new PlanChangeSessionRequest("cus_1", "sub_1", "si_1", "price_pro_yearly", "bpc_change",
            "https://reelshelf.test/settings?billing=portal"), Assert.Single(stripe.PlanChanges));
        Assert.Empty(stripe.Downgrades);
    }

    [Fact]
    public async Task ChangePlan_ToALesserPlan_WaitsForTheEndOfThePaidPeriod()
    {
        var (service, stripe, store) = Subscribed("pro_yearly");

        PlanChangeResult result = await service.ChangePlan(User, "plus_monthly", CancellationToken.None);

        DateTimeOffset periodEnd = stripe.Subscriptions["sub_1"].CurrentPeriodEnd!.Value;
        Assert.Equal(new PlanChangeResult(PlanChangeOutcome.Scheduled, At: periodEnd), result);
        Assert.Equal(("sub_1", "price_plus_monthly", "month"), Assert.Single(stripe.Downgrades));
        Assert.Empty(stripe.PlanChanges);

        // Recorded at once, without waiting for the webhook; the account keeps Pro until then.
        BillingSubscription recorded = Assert.Single(store.Subscriptions);
        Assert.Equal(("plus_monthly", periodEnd), (recorded.ScheduledLookupKey, recorded.ScheduledAt));
        Assert.Equal(AccountTier.Pro, recorded.Tier);
    }

    [Fact]
    public async Task ChangePlan_UpwardsWithADowngradeWaiting_DropsItFirst()
    {
        var (service, stripe, store) = Subscribed("pro_monthly");
        await service.ChangePlan(User, "plus_monthly", CancellationToken.None);

        PlanChangeResult result = await service.ChangePlan(User, "pro_yearly", CancellationToken.None);

        Assert.Equal(PlanChangeOutcome.Confirm, result.Outcome);
        Assert.Equal(["sub_sched_1"], stripe.Released);
        Assert.Null(Assert.Single(store.Subscriptions).ScheduledLookupKey);
    }

    [Fact]
    public async Task ChangePlan_BackToTheCurrentPlan_DropsTheWaitingDowngrade()
    {
        var (service, stripe, store) = Subscribed("pro_monthly");
        await service.ChangePlan(User, "plus_monthly", CancellationToken.None);

        PlanChangeResult result = await service.ChangePlan(User, "pro_monthly", CancellationToken.None);

        Assert.Equal(PlanChangeOutcome.SamePlan, result.Outcome);
        Assert.Equal(["sub_sched_1"], stripe.Released);
        Assert.Null(Assert.Single(store.Subscriptions).ScheduledLookupKey);
    }

    [Fact]
    public async Task KeepPlan_DropsTheWaitingDowngrade_AndIsFalseWithoutOne()
    {
        var (service, stripe, store) = Subscribed("pro_monthly");
        Assert.False(await service.KeepPlan(User, CancellationToken.None));
        await service.ChangePlan(User, "plus_yearly", CancellationToken.None);

        Assert.True(await service.KeepPlan(User, CancellationToken.None));

        Assert.Equal(["sub_sched_1"], stripe.Released);
        Assert.Null(Assert.Single(store.Subscriptions).ScheduledLookupKey);
    }

    [Fact]
    public async Task ChangePlan_IsRefusedWhenItCantApply()
    {
        var (unconfigured, _, _) = Subscribed("plus_monthly", planChangeConfiguration: "");
        Assert.Equal(PlanChangeOutcome.Unavailable,
            (await unconfigured.ChangePlan(User, "pro_monthly", CancellationToken.None)).Outcome);

        var (service, _, store) = Subscribed("plus_monthly");
        Assert.Equal(PlanChangeOutcome.UnknownPlan,
            (await service.ChangePlan(User, "free", CancellationToken.None)).Outcome);
        Assert.Equal(PlanChangeOutcome.SamePlan,
            (await service.ChangePlan(User, "plus_monthly", CancellationToken.None)).Outcome);

        // Set to end, or a payment failing: that is sorted out in the portal first.
        store.Subscriptions[0] = store.Subscriptions[0] with { CancelAtPeriodEnd = true };
        Assert.Equal(PlanChangeOutcome.NotChangeable,
            (await service.ChangePlan(User, "pro_monthly", CancellationToken.None)).Outcome);
        store.Subscriptions[0] = store.Subscriptions[0] with { CancelAtPeriodEnd = false, Status = "past_due" };
        Assert.Equal(PlanChangeOutcome.NotChangeable,
            (await service.ChangePlan(User, "pro_monthly", CancellationToken.None)).Outcome);

        store.Subscriptions.Clear();
        Assert.Equal(PlanChangeOutcome.NotSubscribed,
            (await service.ChangePlan(User, "pro_monthly", CancellationToken.None)).Outcome);
    }
}
