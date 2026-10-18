using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Reelshelf.Worker;

/// <summary>
/// Moves files to and from storage through the job's presigned URLs, streaming to and from disk. It uses its own
/// HTTP client, which never carries the worker secret. Transient failures (network, 5xx, 429) are retried a few
/// times; a job that still can't transfer fails as retryable, so the next claim gets fresh URLs.
/// </summary>
public sealed class MediaTransfer(IHttpClientFactory httpClientFactory, ILogger<MediaTransfer> logger)
{
    public const string HttpClientName = "media";

    private const int Attempts = 4;
    private const int BufferSize = 1024 * 1024;

    /// <summary>The delay before retry <paramref name="attempt"/> (1-based); tests shorten it.</summary>
    internal Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(2 << (attempt - 1));

    /// <summary>
    /// Saves the object at <paramref name="url"/> to <paramref name="path"/>. A missing object, or one larger than
    /// <paramref name="maxBytes"/>, fails the job as bad input.
    /// </summary>
    public async Task<long> DownloadAsync(string url, string path, long maxBytes, CancellationToken cancellationToken)
    {
        return await WithRetriesAsync("download the source", async () =>
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response =
                await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new JobFailedException("The source is not in storage (404)", retryable: false);
            }

            EnsureSuccess(response, "Downloading the source");
            if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            {
                throw TooLarge(maxBytes);
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
                FileOptions.Asynchronous);
            byte[] buffer = new byte[BufferSize];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw TooLarge(maxBytes);
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return total;
        }, cancellationToken);
    }

    /// <summary>
    /// PUTs <paramref name="path"/> to its presigned <paramref name="url"/> with exactly the <c>Content-Type</c> and
    /// <c>Cache-Control</c> the URL signs. Uploading again overwrites, so a re-claimed job can repeat it safely.
    /// </summary>
    public async Task UploadAsync(string url, string path, string contentType, CancellationToken cancellationToken)
    {
        await WithRetriesAsync($"upload {Path.GetFileName(path)}", async () =>
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous);
            using StreamContent content = new(file, BufferSize);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Headers.ContentLength = file.Length;
            using HttpRequestMessage request = new(HttpMethod.Put, url) { Content = content };
            // Added unparsed so it goes out byte for byte as signed, not reordered by the typed header.
            request.Headers.TryAddWithoutValidation("Cache-Control", EncodeArguments.UploadCacheControl);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            EnsureSuccess(response, $"Uploading {Path.GetFileName(path)}");
            return 0;
        }, cancellationToken);
    }

    private async Task<T> WithRetriesAsync<T>(string what, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= Attempts)
                {
                    throw new JobFailedException($"Could not {what}: {exception.Message}", retryable: true, exception);
                }

                TimeSpan delay = RetryDelay(attempt);
                logger.LogWarning("Could not {What} (attempt {Attempt}), retrying in {Delay}: {Error}",
                    what, attempt, delay, exception.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        TransientStorageException => true,
        HttpRequestException => true,
        IOException => true,
        // HttpClient's own timeout, not our cancellation (which the caller's filter excludes).
        TaskCanceledException => true,
        _ => false
    };

    private static void EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;
        if (status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TransientStorageException($"{what} failed with {status}");
        }

        // A 403 is an expired or mis-signed URL; a fresh claim presigns new ones.
        throw new JobFailedException($"{what} failed with {status}", retryable: true);
    }

    private static JobFailedException TooLarge(long maxBytes) =>
        new($"The source is larger than the {maxBytes}-byte limit (Worker:MaxSourceBytes)", retryable: false);

    private sealed class TransientStorageException(string message) : Exception(message);
}
