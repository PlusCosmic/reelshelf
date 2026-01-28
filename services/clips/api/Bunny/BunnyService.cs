using Nucleus.Clips.Bunny.Models;

namespace Nucleus.Clips.Bunny;

public class BunnyService
{
    private readonly string _collectionsUrl;

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    private readonly string _videosUrl;

    public BunnyService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        var baseUrl =
            $"https://video.bunnycdn.com/library/{configuration["BunnyLibraryId"] ?? throw new InvalidOperationException("Bunny API library ID not configured")}";
        _collectionsUrl = baseUrl + "/collections";
        _videosUrl = baseUrl + "/videos";

        _httpClient.DefaultRequestHeaders.Add("AccessKey",
            configuration["BunnyAccessKey"] ?? throw new InvalidOperationException("Bunny access key not configured"));
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<BunnyCollection> CreateCollectionAsync(string categorySlug, Guid userId)
    {
        var env = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "";
        var content = JsonContent.Create(
            new CreateCollectionRequest(env + "-" + categorySlug + "-" + userId));
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json");
        var collectionResponse = await _httpClient.PostAsync(_collectionsUrl, content);
        var response = await collectionResponse.Content.ReadFromJsonAsync<BunnyCollection>();
        return response ?? throw new InvalidOperationException("Failed to deserialize bunny collection");
    }

    public async Task<PagedVideoResponse> GetVideosForCollectionAsync(Guid collectionId, int page, int pageSize,
        string? titleSearch = null)
    {
        var searchString = titleSearch != null ? $"&search={titleSearch}" : "";
        var url = _videosUrl + $"?collection={collectionId}&page={page}&itemsPerPage={pageSize}{searchString}";
        var videosResponse = await _httpClient.GetAsync(url);
        var pagedResponse = await videosResponse.Content.ReadFromJsonAsync<PagedVideoResponse>() ??
                            throw new InvalidOperationException("Failed to deserialize bunny videos");
        return pagedResponse;
    }

    public async Task<BunnyVideo> CreateVideoAsync(Guid collectionId, string videoTitle)
    {
        var url = _videosUrl;

        var request = new CreateVideoRequest(videoTitle, collectionId.ToString(), 0);
        var content = JsonContent.Create(request);
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json");
        var videoResponse = await _httpClient.PostAsync(url, content);
        var response = await videoResponse.Content.ReadFromJsonAsync<BunnyVideo>();
        return response ?? throw new InvalidOperationException("Failed to deserialize bunny video");
    }

    public async Task<BunnyVideo?> GetVideoByIdAsync(Guid videoId)
    {
        var url = _videosUrl + $"/{videoId}";
        var videoResponse = await _httpClient.GetAsync(url);
        var response = await videoResponse.Content.ReadFromJsonAsync<BunnyVideo>();
        return response;
    }

    public async Task UpdateVideoTitleAsync(Guid videoId, string newTitle)
    {
        var url = _videosUrl + $"/{videoId}";
        var content = JsonContent.Create(new { title = newTitle });
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteVideoAsync(Guid videoId)
    {
        var url = _videosUrl + $"/{videoId}";
        var response = await _httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }
}
