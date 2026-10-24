using System.Text.Json;

namespace Reelshelf.Billing;

/// <summary>
/// The parts of a Stripe webhook event billing acts on, read from the verified payload. Billing never trusts the
/// event's copy of a subscription: it only learns which subscription changed and reads it from Stripe again, so
/// events arriving late, twice or out of order all end in Stripe's current state. Reading the JSON directly keeps
/// this working whatever API version the endpoint is set to.
/// </summary>
public sealed record BillingWebhookEvent(
    string Id,
    string Type,
    string? SubscriptionId,
    string? CustomerId,
    Guid? UserId)
{
    /// <summary>The event types the webhook endpoint should send; anything else is acknowledged and ignored.</summary>
    public static readonly IReadOnlySet<string> HandledTypes = new HashSet<string>
    {
        "checkout.session.completed",
        "customer.subscription.created",
        "customer.subscription.updated",
        "customer.subscription.deleted",
        "customer.subscription.paused",
        "customer.subscription.resumed",
        "invoice.paid",
        "invoice.payment_failed",
        "subscription_schedule.created",
        "subscription_schedule.updated",
        "subscription_schedule.released",
        "subscription_schedule.canceled",
        "subscription_schedule.completed"
    };

    public static BillingWebhookEvent Parse(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        string id = root.GetProperty("id").GetString() ?? "";
        string type = root.GetProperty("type").GetString() ?? "";
        if (!root.TryGetProperty("data", out JsonElement data) || !data.TryGetProperty("object", out JsonElement obj))
        {
            return new BillingWebhookEvent(id, type, null, null, null);
        }

        if (type == "checkout.session.completed")
        {
            Guid? userId = Guid.TryParse(StringProperty(obj, "client_reference_id"), out Guid parsed) ? parsed : null;
            return new BillingWebhookEvent(id, type, ReferenceId(obj, "subscription"), ReferenceId(obj, "customer"), userId);
        }

        if (type.StartsWith("customer.subscription.", StringComparison.Ordinal))
        {
            return new BillingWebhookEvent(id, type, StringProperty(obj, "id"), ReferenceId(obj, "customer"), null);
        }

        if (type.StartsWith("subscription_schedule.", StringComparison.Ordinal))
        {
            // A released schedule names its subscription under released_subscription instead.
            return new BillingWebhookEvent(id, type,
                ReferenceId(obj, "subscription") ?? ReferenceId(obj, "released_subscription"),
                ReferenceId(obj, "customer"), null);
        }

        if (type.StartsWith("invoice.", StringComparison.Ordinal))
        {
            // Since API version 2025-03-31 an invoice names its subscription under parent.subscription_details.
            string? subscriptionId = obj.TryGetProperty("parent", out JsonElement parent) &&
                                     parent.ValueKind == JsonValueKind.Object &&
                                     parent.TryGetProperty("subscription_details", out JsonElement details) &&
                                     details.ValueKind == JsonValueKind.Object
                ? ReferenceId(details, "subscription")
                : ReferenceId(obj, "subscription");
            return new BillingWebhookEvent(id, type, subscriptionId, ReferenceId(obj, "customer"), null);
        }

        return new BillingWebhookEvent(id, type, null, null, null);
    }

    private static string? StringProperty(JsonElement obj, string property)
    {
        return obj.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>A reference that is either an id or, when expanded, an object with one.</summary>
    private static string? ReferenceId(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object => StringProperty(value, "id"),
            _ => null
        };
    }
}
