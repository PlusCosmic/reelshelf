using Microsoft.AspNetCore.Authentication;
using Reelshelf.Auth;

namespace Reelshelf.Devices;

public static class DeviceSetup
{
    /// <summary>
    /// Device sign-in (ADR-0010): the <c>DeviceToken</c> scheme, the <see cref="AuthorizationPolicies.CookieOrDevice"/>
    /// and <see cref="AuthorizationPolicies.Device"/> policies, the uploader's version gate and the daily sweep (never
    /// in <c>OpenApi</c>).
    /// </summary>
    public static void AddDevices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DeviceOptions>(builder.Configuration.GetSection(DeviceOptions.SectionName));
        builder.Services.Configure<UploaderOptions>(builder.Configuration.GetSection(UploaderOptions.SectionName));
        builder.Services.AddScoped<DeviceStatements>();
        builder.Services.AddScoped<DeviceSignInService>();
        builder.Services.AddScoped<DeviceTokenSweeper>();

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthentication>(
                DeviceTokenAuthentication.SchemeName, configureOptions: null);
        builder.Services.AddAuthorizationBuilder().AddDevicePolicies();

        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<DeviceTokenSweepService>();
        }
    }

    /// <summary>The uploader's version gate, before anything else under <c>/api</c> runs.</summary>
    public static IApplicationBuilder UseClientVersionGate(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientVersionGate>();
}
