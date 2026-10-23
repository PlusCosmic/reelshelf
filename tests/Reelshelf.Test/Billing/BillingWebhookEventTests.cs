using Reelshelf.Billing;
using Xunit;

namespace Reelshelf.Test.Billing;

public class BillingWebhookEventTests
{
    [Fact]
    public void CheckoutCompleted_CarriesTheAccountCustomerAndSubscription()
    {
        Guid userId = Guid.NewGuid();
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse($$$$"""
            {"id":"evt_1","type":"checkout.session.completed","data":{"object":{"object":"checkout.session",
            "client_reference_id":"{{{{userId}}}}","customer":"cus_1","subscription":"sub_1","mode":"subscription"}}}
            """);

        Assert.Equal(new BillingWebhookEvent("evt_1", "checkout.session.completed", "sub_1", "cus_1", userId), parsed);
    }

    [Fact]
    public void CheckoutCompleted_WithoutAnAccountId_HasNoAccount()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_1","type":"checkout.session.completed","data":{"object":{"client_reference_id":null,
            "customer":"cus_1","subscription":"sub_1"}}}
            """);

        Assert.Null(parsed.UserId);
        Assert.Equal("sub_1", parsed.SubscriptionId);
    }

    [Fact]
    public void SubscriptionEvent_IsAboutThatSubscription()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_2","type":"customer.subscription.updated","data":{"object":{"id":"sub_2","object":"subscription",
            "customer":{"id":"cus_2","object":"customer"},"status":"active"}}}
            """);

        Assert.Equal(new BillingWebhookEvent("evt_2", "customer.subscription.updated", "sub_2", "cus_2", null), parsed);
    }

    [Fact]
    public void Invoice_NamesItsSubscriptionUnderParent()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_3","type":"invoice.payment_failed","data":{"object":{"id":"in_3","customer":"cus_3",
            "parent":{"type":"subscription_details","subscription_details":{"subscription":"sub_3"}}}}}
            """);

        Assert.Equal("sub_3", parsed.SubscriptionId);
        Assert.Equal("cus_3", parsed.CustomerId);
    }

    [Fact]
    public void Invoice_OnAnOlderApiVersion_NamesItsSubscriptionDirectly()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_4","type":"invoice.paid","data":{"object":{"id":"in_4","customer":"cus_4","subscription":"sub_4"}}}
            """);

        Assert.Equal("sub_4", parsed.SubscriptionId);
    }

    [Fact]
    public void AnInvoiceWithoutASubscription_HasNone()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_5","type":"invoice.paid","data":{"object":{"id":"in_5","customer":"cus_5","parent":null}}}
            """);

        Assert.Null(parsed.SubscriptionId);
    }

    [Fact]
    public void OtherEvents_CarryOnlyTheirType()
    {
        BillingWebhookEvent parsed = BillingWebhookEvent.Parse("""
            {"id":"evt_6","type":"charge.refunded","data":{"object":{"id":"ch_6","customer":"cus_6"}}}
            """);

        Assert.Equal(new BillingWebhookEvent("evt_6", "charge.refunded", null, null, null), parsed);
    }
}
