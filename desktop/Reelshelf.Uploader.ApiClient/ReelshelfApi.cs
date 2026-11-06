using System.Net;
using System.Text.Json;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Serialization.Json;

namespace Reelshelf.Uploader.ApiClient;

/// <summary>
/// What one call to the API answered: its status, the parsed body on success, and the problem's title and detail
/// (or an RFC 8628 <c>error</c>) otherwise. A request that never got an answer has no status.
/// </summary>
public sealed record ApiResult<T>(HttpStatusCode? Status, T? Value, string? Title, string? Detail, string? Error)
{
    public bool IsSuccess => Status is { } status && (int)status is >= 200 and < 300;

    /// <summary>The network failed, or the server did (5xx): worth trying again later.</summary>
    public bool IsTransient => Status is null || (int)Status >= 500 || Status == HttpStatusCode.TooManyRequests;
}

/// <summary>
/// The generated client (<see cref="ReelshelfApiClient"/>) with the answers the uploader needs to tell apart. Kiota
/// builds each request; this sends it and reads the status and body itself, because the uploader acts on 403, 409,
/// 422, 426 and 503 differently, and Kiota would turn all of them into the same exception.
/// </summary>
public sealed class ReelshelfApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientRequestAdapter _adapter;

    public ReelshelfApi(Uri baseUrl, ReelshelfClientHandler handler)
    {
        Handler = handler;
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        _adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: _http)
        {
            BaseUrl = baseUrl.ToString().TrimEnd('/')
        };
        Client = new ReelshelfApiClient(_adapter);
    }

    public ReelshelfClientHandler Handler { get; }

    /// <summary>Builds requests; send them with <see cref="SendAsync{T}"/> or <see cref="SendAsync"/>.</summary>
    public ReelshelfApiClient Client { get; }

    public Task<ApiResult<T>> SendAsync<T>(RequestInformation request, ParsableFactory<T> factory,
        CancellationToken cancellationToken) where T : IParsable =>
        SendParsedAsync(request, node => node.GetObjectValue(factory), cancellationToken);

    /// <summary>A request answered with a JSON array.</summary>
    public Task<ApiResult<List<T>>> SendCollectionAsync<T>(RequestInformation request, ParsableFactory<T> factory,
        CancellationToken cancellationToken) where T : IParsable =>
        SendParsedAsync(request, node => node.GetCollectionOfObjectValues(factory).ToList(), cancellationToken);

    private async Task<ApiResult<T>> SendParsedAsync<T>(RequestInformation request, Func<IParseNode, T?> read,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage message =
                await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(request, cancellationToken)
                ?? throw new InvalidOperationException("Kiota built no request");
            using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                T? value = default;
                if (body.Length > 0)
                {
                    using MemoryStream stream = new(body);
                    IParseNode node = await new JsonParseNodeFactory()
                        .GetRootParseNodeAsync("application/json", stream, cancellationToken);
                    value = read(node);
                }

                return new ApiResult<T>(response.StatusCode, value, null, null, null);
            }

            (string? title, string? detail, string? error) = ReadProblem(body);
            return new ApiResult<T>(response.StatusCode, default, title, detail, error);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T>(null, default, null, null, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's timeout.
            return new ApiResult<T>(null, default, null, null, null);
        }
    }

    /// <summary>A request with no body to read back.</summary>
    public async Task<ApiResult<object>> SendAsync(RequestInformation request, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage message =
                await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(request, cancellationToken)
                ?? throw new InvalidOperationException("Kiota built no request");
            using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            (string? title, string? detail, string? error) = response.IsSuccessStatusCode ? default : ReadProblem(body);
            return new ApiResult<object>(response.StatusCode, null, title, detail, error);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<object>(null, null, null, null, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiResult<object>(null, null, null, null, null);
        }
    }

    /// <summary>A problem details body's title and detail, an <c>{ error }</c> body's error, or a plain string.</summary>
    private static (string? Title, string? Detail, string? Error) ReadProblem(byte[] body)
    {
        if (body.Length == 0)
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                return (null, root.GetString(), null);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            return (String(root, "title"), String(root, "detail"), String(root, "error"));
        }
        catch (JsonException)
        {
            return default;
        }

        static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
