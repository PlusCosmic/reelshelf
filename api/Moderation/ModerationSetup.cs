using Reelshelf.Plans;
using Reelshelf.Video.R2;

namespace Reelshelf.Moderation;

public static class ModerationSetup
{
    /// <summary>
    /// Reports, takedowns and removals. Removing a clip needs the private evidence bucket (<c>Moderation:EvidenceBucket</c>,
    /// reached with the <c>R2:*</c> credentials); without it the queue still works but clips can't be removed. Purging
    /// the media domain needs <c>Moderation:Cloudflare</c>. The evidence service never runs in <c>OpenApi</c>.
    /// </summary>
    public static void AddModeration(this WebApplicationBuilder builder)
    {
        IConfigurationSection section = builder.Configuration.GetSection(ModerationOptions.SectionName);
        builder.Services.Configure<ModerationOptions>(section);
        ModerationOptions options = section.Get<ModerationOptions>() ?? new ModerationOptions();
        R2Options? r2 = R2Options.FromConfiguration(builder.Configuration);

        builder.Services.AddScoped<ContentReportStatements>();
        builder.Services.AddScoped<ContentReportService>();
        builder.Services.AddScoped<IClipEvidenceStore, ClipEvidenceStatements>();
        builder.Services.AddSingleton<ClipEvidenceSignal>();
        builder.Services.AddScoped<IEvidenceMediaDeleter, EvidenceMediaDeleter>();
        builder.Services.AddScoped<ClipEvidenceProcessor>();

        if (r2 is not null && options.EvidenceBucket.Length > 0)
        {
            R2Options evidenceBucket = new()
            {
                AccountId = r2.AccountId,
                AccessKeyId = r2.AccessKeyId,
                SecretAccessKey = r2.SecretAccessKey,
                Bucket = options.EvidenceBucket,
                MediaBaseUrl = r2.MediaBaseUrl
            };
            builder.Services.AddSingleton<IEvidenceStorage>(provider => new R2EvidenceStorage(
                new R2Storage(evidenceBucket, provider.GetRequiredService<ILogger<R2Storage>>())));
        }
        else
        {
            builder.Services.AddSingleton<IEvidenceStorage, UnconfiguredEvidenceStorage>();
        }

        if (r2 is null)
        {
            builder.Services.AddSingleton<ICdnPurger>(new NoCdnPurger(CdnPurgeResults.NotNeeded));
        }
        else if (options.Cloudflare.IsConfigured)
        {
            builder.Services.AddHttpClient(CloudflareCdnPurger.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(30));
            builder.Services.AddSingleton<ICdnPurger>(provider => new CloudflareCdnPurger(
                provider.GetRequiredService<IHttpClientFactory>(), r2, options.Cloudflare));
        }
        else
        {
            builder.Services.AddSingleton<ICdnPurger>(new NoCdnPurger(CdnPurgeResults.NotConfigured));
        }

        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<ClipEvidenceBackgroundService>();
        }
    }
}
