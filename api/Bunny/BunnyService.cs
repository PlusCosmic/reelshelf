using Reelshelf.Bunny.Models;
using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Video;

namespace Reelshelf.Bunny;

/// <summary>
/// Bunny Stream as the clip video host. Videos are filed in one Bunny collection per owner and game category, kept
/// in <c>clip_collection</c>; a collection is the host's container.
/// </summary>
public class BunnyService : IClipVideoHost
{
    private readonly string _collectionsUrl;

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ClipsStatements _clipsStatements;
    private readonly ILogger<BunnyService> _logger;

    private readonly string _videosUrl;

    public BunnyService(HttpClient httpClient, IConfiguration configuration, ClipsStatements clipsStatements,
        ILogger<BunnyService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _clipsStatements = clipsStatements;
        _logger = logger;
        var baseUrl =
            $"https://video.bunnycdn.com/library/{configuration["BunnyLibraryId"] ?? throw new InvalidOperationException("Bunny API library ID not configured")}";
        _collectionsUrl = baseUrl + "/collections";
        _videosUrl = baseUrl + "/videos";

        _httpClient.DefaultRequestHeaders.Add("AccessKey",
            configuration["BunnyAccessKey"] ?? throw new InvalidOperationException("Bunny access key not configured"));
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<HostedVideo> CreateVideoAsync(Guid ownerId, GameCategory gameCategory, string title)
    {
        ClipsStatements.ClipCollectionRow clipCollection =
            await _clipsStatements.GetCollectionByOwnerAndCategory(ownerId, gameCategory.Id)
            ?? await GetOrCreateCollectionAsync(ownerId, gameCategory);

        var request = new CreateVideoRequest(title, clipCollection.CollectionId.ToString(), 0);
        var content = JsonContent.Create(request);
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json");
        var videoResponse = await _httpClient.PostAsync(_videosUrl, content);
        videoResponse.EnsureSuccessStatusCode();
        var video = await videoResponse.Content.ReadFromJsonAsync<BunnyVideo>()
                    ?? throw new InvalidOperationException("Failed to deserialize bunny video");
        return new HostedVideo(video.Guid, video.CollectionId, ToHostedVideoInfo(video));
    }

    public UploadTarget CreateUploadTarget(HostedVideo video, string title)
    {
        string libraryId = _configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");
        string accessKey = _configuration["BunnyAccessKey"]
                           ?? throw new InvalidOperationException("Bunny access key not configured");
        return BunnyUploadTarget.Create(libraryId, accessKey, video, title, DateTimeOffset.UtcNow);
    }

    private async Task<ClipsStatements.ClipCollectionRow> GetOrCreateCollectionAsync(Guid ownerId, GameCategory gameCategory)
    {
        BunnyCollection bunnyCollection = await CreateCollectionAsync(gameCategory.Slug, ownerId);
        ClipsStatements.ClipCollectionRow collection =
            await _clipsStatements.InsertCollection(ownerId, bunnyCollection.Guid, gameCategory.Id);
        if (collection.CollectionId != bunnyCollection.Guid)
        {
            // A concurrent upload created the category's collection first; ours is empty, so drop it.
            try
            {
                await DeleteContainerAsync(bunnyCollection.Guid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete unused Bunny collection {CollectionId}", bunnyCollection.Guid);
            }
        }

        return collection;
    }

    private async Task<BunnyCollection> CreateCollectionAsync(string categorySlug, Guid userId)
    {
        var env = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "";
        var content = JsonContent.Create(
            new CreateCollectionRequest(env + "-" + categorySlug + "-" + userId));
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json");
        var collectionResponse = await _httpClient.PostAsync(_collectionsUrl, content);
        collectionResponse.EnsureSuccessStatusCode();
        var response = await collectionResponse.Content.ReadFromJsonAsync<BunnyCollection>();
        return response ?? throw new InvalidOperationException("Failed to deserialize bunny collection");
    }

    public async Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        var url = _videosUrl + $"/{videoId}";
        using StreamContent streamContent = new(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        if (contentLength is { } length)
        {
            streamContent.Headers.ContentLength = length;
        }

        using HttpResponseMessage response = await _httpClient.PutAsync(url, streamContent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<HostedVideoInfo?> GetVideoAsync(Guid videoId)
    {
        var url = _videosUrl + $"/{videoId}";
        var videoResponse = await _httpClient.GetAsync(url);
        if (videoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        videoResponse.EnsureSuccessStatusCode();
        var response = await videoResponse.Content.ReadFromJsonAsync<BunnyVideo>();
        return response is null ? null : ToHostedVideoInfo(response);
    }

    /// <summary>
    /// Idempotent: a video Bunny no longer knows about counts as deleted, so a retried clip delete whose
    /// earlier attempt removed the video but not the row can still complete.
    /// </summary>
    public async Task DeleteVideoAsync(Guid videoId)
    {
        var url = _videosUrl + $"/{videoId}";
        var response = await _httpClient.DeleteAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Deletes a Bunny collection. Idempotent like <see cref="DeleteVideoAsync"/>: a collection Bunny no longer knows about counts as deleted.</summary>
    public async Task DeleteContainerAsync(Guid containerId)
    {
        var url = _collectionsUrl + $"/{containerId}";
        var response = await _httpClient.DeleteAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public static HostedVideoInfo ToHostedVideoInfo(BunnyVideo video)
    {
        return new HostedVideoInfo(
            video.Title,
            video.Length,
            video.Status,
            video.EncodeProgress,
            video.StorageSize,
            video.DateUploaded,
            video.ThumbnailFileName);
    }
}
