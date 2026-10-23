using Stripe;

namespace Reelshelf.Billing;

public static class BillingSetup
{
    /// <summary>
    /// Registers billing. The tier service and the store are always there, so every account resolves to Free or
    /// Unlimited without Stripe; Stripe itself only when <c>Billing:SecretKey</c> is set (<see cref="BillingOptions"/>).
    /// </summary>
    public static void AddBilling(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IBillingStore, BillingStatements>();
        builder.Services.AddScoped<AccountTierService>();

        BillingOptions? options = BillingOptions.FromConfiguration(builder.Configuration);
        if (options is null)
        {
            builder.Services.AddSingleton<IAccountSubscriptions, NoAccountSubscriptions>();
            return;
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new StripeClient(options.SecretKey));
        builder.Services.AddSingleton<IStripeBilling, StripeBilling>();
        builder.Services.AddScoped<BillingService>();
        builder.Services.AddScoped<IAccountSubscriptions>(provider => provider.GetRequiredService<BillingService>());
    }
}
