using Reelshelf.Plans;
using Reelshelf.Video.R2;

namespace Reelshelf.Moderation;

public static class ModerationSetup
{
    /// <summary>
    /// Reports, takedowns and removals. Removing a clip needs the private evidence bucket (<c>Moderation:EvidenceBucket</c>,
    /// reached through the media bucket's store and credentials, <c>Storage:*</c> or <c>R2:*</c>); without it the queue
    /// still works but clips can't be removed. Purging the media domain needs <c>Moderation:Cloudflare</c> (a zone) or
    /// <c>Moderation:CloudFront</c> (a distribution, invalidated with the store's own AWS credentials), whichever CDN
    /// serves it; setting both fails startup. The evidence service never runs in <c>OpenApi</c>.
    /// </summary>
    public static void AddModeration(this WebApplicationBuilder builder)
    {
        IConfigurationSection section = builder.Configuration.GetSection(ModerationOptions.SectionName);
        builder.Services.Configure<ModerationOptions>(section);
        ModerationOptions options = section.Get<ModerationOptions>() ?? new ModerationOptions();
        R2Options? r2 = R2Options.FromConfiguration(builder.Configuration);
        if (options.CloudFront.IsConfigured && options.Cloudflare.IsSet)
        {
            throw new InvalidOperationException(
                "Moderation:CloudFront and Moderation:Cloudflare are both set; the media domain is on one CDN, so set " +
                "only the one that serves it");
        }

        builder.Services.AddScoped<ContentReportStatements>();
        builder.Services.AddScoped<ContentReportService>();
        builder.Services.AddScoped<IClipEvidenceStore, ClipEvidenceStatements>();
        builder.Services.AddSingleton<ClipEvidenceSignal>();
        builder.Services.AddScoped<IEvidenceMediaDeleter, EvidenceMediaDeleter>();
        builder.Services.AddScoped<ClipEvidenceProcessor>();

        if (r2 is not null && options.EvidenceBucket.Length > 0)
        {
            R2Options evidenceBucket = r2.WithBucket(options.EvidenceBucket);
            builder.Services.AddSingleton<IEvidenceStorage>(provider => new R2EvidenceStorage(
                new R2Storage(evidenceBucket, provider.GetRequiredService<ILogger<R2Storage>>())));
        }
        else
        {
            builder.Services.AddSingleton<IEvidenceStorage, UnconfiguredEvidenceStorage>();
        }

        builder.Services.AddSingleton<IEvidenceQuarantine>(provider =>
            provider.GetService<R2Storage>() is { } media ? new R2EvidenceQuarantine(media) : new NoEvidenceQuarantine());

        if (r2 is null)
        {
            builder.Services.AddSingleton<ICdnPurger>(new NoCdnPurger(CdnPurgeResults.NotNeeded));
        }
        else if (options.CloudFront.IsConfigured)
        {
            if (r2.ServiceUrl is not null)
            {
                // Its keys are the endpoint's own, not AWS's, so every invalidation would be refused, and a failing
                // purge holds up the evidence copy behind it.
                throw new InvalidOperationException(
                    "Moderation:CloudFront:DistributionId needs the object store on AWS S3 (Storage:Region without " +
                    "Storage:Endpoint), whose credentials sign the invalidations");
            }

            string distributionId = options.CloudFront.DistributionId.Trim();
            builder.Services.AddSingleton<ICdnPurger>(_ => new CloudFrontCdnPurger(r2, distributionId));
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
