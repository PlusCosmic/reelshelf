namespace Reelshelf.Billing;

/// <summary>
/// Stripe settings. Billing is off (its endpoints answer 404 and everyone is Free or Unlimited) until
/// <c>Billing:SecretKey</c> is set; a key without <c>Billing:WebhookSecret</c> fails startup, since subscriptions
/// would then be taken without ever being recorded.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>A restricted key (<c>rk_</c>) with the permissions in docs/billing.md, or a secret key.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>The signing secret of the webhook endpoint (<c>whsec_</c>).</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Where Checkout and the Customer Portal send people back to; the API's <c>FrontendOrigin</c>.</summary>
    public string FrontendOrigin { get; set; } = "http://localhost:5173";

    public static BillingOptions? FromConfiguration(IConfiguration configuration)
    {
        BillingOptions options = new();
        configuration.GetSection(SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            throw new InvalidOperationException(
                "Billing:SecretKey is set without Billing:WebhookSecret. Set both, or neither to leave billing off.");
        }

        options.FrontendOrigin = (configuration["FrontendOrigin"] ?? options.FrontendOrigin).TrimEnd('/');
        return options;
    }
}
