using System.Net.Http.Headers;
using System.Net.Http.Json;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Moderation;

/// <summary>The private bucket removed clips are kept in (<c>Moderation:EvidenceBucket</c>).</summary>
public interface IEvidenceStorage
{
    bool IsConfigured { get; }

    Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task DeletePrefixAsync(string prefix);
}

public sealed class R2EvidenceStorage(R2Storage bucket) : IEvidenceStorage
{
    public bool IsConfigured => true;

    public Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        bucket.UploadAsync(key, content, contentType, cancellationToken);

    public Task DeletePrefixAsync(string prefix) => bucket.DeletePrefixAsync(prefix);
}

public sealed class UnconfiguredEvidenceStorage : IEvidenceStorage
{
    public bool IsConfigured => false;

    public Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Moderation:EvidenceBucket isn't set");

    public Task DeletePrefixAsync(string prefix) => Task.CompletedTask;
}

public static class CdnPurgeResults
{
    public const string Purged = "purged";

    /// <summary>R2 is on but no Cloudflare token is set: the media domain may serve cached copies until purged by hand.</summary>
    public const string NotConfigured = "not_configured";

    /// <summary>R2 isn't configured, so nothing of the clip was ever on the media domain.</summary>
    public const string NotNeeded = "not_needed";
}

/// <summary>
/// Purges a removed clip's files from the media domain's cache. Its outputs are cached <c>immutable</c> for a year, so
/// deleting them from R2 alone would leave Cloudflare serving them.
/// </summary>
public interface ICdnPurger
{
    /// <summary>Purges every output of versions 1 to <paramref name="maxVersion"/>; returns a <see cref="CdnPurgeResults"/> value.</summary>
    Task<string> PurgeClipAsync(Guid clipId, int maxVersion, CancellationToken cancellationToken);
}

/// <summary>Purges by URL through Cloudflare's API, which every plan allows, 30 URLs a request.</summary>
public sealed class CloudflareCdnPurger(
    IHttpClientFactory httpClientFactory,
    R2Options r2,
    ModerationOptions.CloudflareOptions cloudflare) : ICdnPurger
{
    public const string HttpClientName = "cloudflare-purge";
    private const int UrlsPerRequest = 30;

    public static IReadOnlyList<string> ClipUrls(string mediaBaseUrl, Guid clipId, int maxVersion) =>
        Enumerable.Range(1, Math.Max(1, maxVersion))
            .SelectMany(version => R2ObjectKeys.AllOutputs.Select(file =>
                $"{mediaBaseUrl.TrimEnd('/')}/{R2ObjectKeys.Output(clipId, version, file)}"))
            .ToList();

    public async Task<string> PurgeClipAsync(Guid clipId, int maxVersion, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        foreach (string[] batch in ClipUrls(r2.MediaBaseUrl, clipId, maxVersion).Chunk(UrlsPerRequest))
        {
            using HttpRequestMessage request = new(HttpMethod.Post,
                $"https://api.cloudflare.com/client/v4/zones/{cloudflare.ZoneId}/purge_cache")
            {
                Content = JsonContent.Create(new { files = batch })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cloudflare.ApiToken);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Cloudflare refused the purge ({(int)response.StatusCode}): {body}");
            }
        }

        return CdnPurgeResults.Purged;
    }
}

public sealed class NoCdnPurger(string result) : ICdnPurger
{
    public Task<string> PurgeClipAsync(Guid clipId, int maxVersion, CancellationToken cancellationToken) =>
        Task.FromResult(result);
}

/// <summary>Deletes a removed clip's video wherever it is public: its host, its R2 versions and any Bunny copy.</summary>
public interface IEvidenceMediaDeleter
{
    Task DeleteAsync(EvidenceClip clip);

    /// <summary>The second sweep: whatever a worker uploaded into the clip's R2 folder after the first delete.</summary>
    Task SweepAsync(Guid clipId);
}

public sealed class EvidenceMediaDeleter(ClipVideoHosts videoHosts, ITranscodeStorage transcodeStorage)
    : IEvidenceMediaDeleter
{
    public async Task DeleteAsync(EvidenceClip clip)
    {
        await ClipVideoDeletion.DeleteAsync(videoHosts, transcodeStorage, clip.Id, clip.VideoHost, clip.VideoId,
            clip.BunnyVideoId);
        // An R2 clip's own delete covers this; a Bunny clip may have R2 versions from a migrate job too.
        await transcodeStorage.DeleteAllVersionsAsync(clip.Id);
    }

    public Task SweepAsync(Guid clipId) => transcodeStorage.DeleteAllVersionsAsync(clipId);
}
