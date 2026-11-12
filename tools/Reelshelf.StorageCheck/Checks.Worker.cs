using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.FFmpeg;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;
using Reelshelf.Worker;

namespace Reelshelf.StorageCheck;

public sealed partial class StorageCheckRunner
{
    /// <summary>The worker's outputs for the run's clip, version 1, by file name, once uploaded.</summary>
    private readonly Dictionary<string, byte[]> _outputs = new(StringComparer.Ordinal);

    /// <summary>
    /// A transcode's storage, as the claim hands it out and the worker uses it: <see cref="R2Storage.UrlsFor"/> with
    /// the API's default presign lifetime, then the worker's own <see cref="MediaTransfer"/>, whose requests are the
    /// real ones byte for byte: a streamed GET of the source, and each output PUT from a file with a known
    /// <c>Content-Length</c>, the typed <c>Content-Type</c> and the signed <c>Cache-Control</c> added unparsed.
    /// </summary>
    private async Task WorkerAsync()
    {
        if (_source is not { } source)
        {
            SkipRest("worker", "there is no uploaded source");
            return;
        }

        TranscodeJobUrls? urls = null;
        bool presigned = await Check("worker.urls", "presign a transcode's URLs (UrlsFor, as the claim does)", () =>
        {
            urls = _media.UrlsFor(_run.ClipId, TranscodeRunTypes.Transcode, 1, 1, new TranscodingOptions().PresignLifetime);
            Expect(urls.SourceUrl is not null, "no source URL");
            Expect(urls.UploadUrls is not null &&
                   urls.UploadUrls.Keys.Order().SequenceEqual(R2ObjectKeys.OutputsFor(TranscodeRunTypes.Transcode).Order()),
                "the upload URLs don't name every output");
            return Task.FromResult($"a source GET and {urls.UploadUrls!.Count} output PUTs");
        });
        if (!presigned || urls?.UploadUrls is null)
        {
            SkipRest("worker", "no URLs");
            return;
        }

        MediaTransfer transfer = new(WorkerHttp(), NullLogger<MediaTransfer>.Instance);
        await Check("worker.download", "the worker downloads the source (MediaTransfer.DownloadAsync)", async () =>
        {
            string path = Path.Combine(_workDirectory, "source");
            long size = await transfer.DownloadAsync(urls.SourceUrl!, path, new WorkerSettings().MaxSourceBytes, _cancellation);
            byte[] downloaded = await File.ReadAllBytesAsync(path, _cancellation);
            File.Delete(path);
            Expect(size == source.Length, $"downloaded {size} bytes of {source.Length}");
            Expect(Hash(downloaded) == Hash(source), "the downloaded bytes differ from the upload");
            return $"{Mib(size)}, same bytes";
        });

        bool allUploaded = true;
        foreach (string fileName in R2ObjectKeys.OutputsFor(TranscodeRunTypes.Transcode))
        {
            // av1.mp4 is above a part, so the export below makes a real multipart upload from it.
            byte[] content = RandomBytes(fileName switch
            {
                R2ObjectKeys.Av1 => MultipartUploadParts.MinPartSize + 128 * Kibibyte,
                R2ObjectKeys.Preview => 64 * Kibibyte,
                _ => 4 * Kibibyte
            });
            allUploaded &= await Check($"worker.put.{fileName}", $"the worker uploads {fileName} (MediaTransfer.UploadAsync)",
                async () =>
                {
                    string path = Path.Combine(_workDirectory, fileName);
                    await File.WriteAllBytesAsync(path, content, _cancellation);
                    await transfer.UploadAsync(urls.UploadUrls[fileName], path, OutputFiles.ContentType(fileName), _cancellation);
                    _outputs[fileName] = content;
                    return $"{Mib(content.Length)} as {OutputFiles.ContentType(fileName)}";
                });
        }

        if (!_outputs.ContainsKey(R2ObjectKeys.Av1) || !_outputs.ContainsKey(R2ObjectKeys.Thumbnail))
        {
            SkipRest("worker", "the outputs didn't upload");
            return;
        }

        await Check("worker.stored", "outputs are stored with the signed headers", async () =>
        {
            List<string> seen = [];
            foreach (string fileName in new[] { R2ObjectKeys.Av1, R2ObjectKeys.Thumbnail })
            {
                Fetched head = await GetAsync(_raw,
                    _media.PresignGet(R2ObjectKeys.Output(_run.ClipId, 1, fileName), TimeSpan.FromMinutes(10)), "bytes=0-0");
                Expect(head.Status == HttpStatusCode.PartialContent, $"{fileName}: {head.Describe()}");
                Expect(head["Content-Type"] == R2ObjectKeys.ContentType(fileName),
                    $"{fileName} is stored as '{head["Content-Type"]}'");
                Expect(head["Cache-Control"] == R2ObjectKeys.ImmutableCacheControl,
                    $"{fileName} is stored with Cache-Control '{head["Cache-Control"]}'");
                seen.Add($"{fileName} {head["Content-Type"]}");
            }

            await using Stream? thumbnail = await _media.OpenReadAsync(
                R2ObjectKeys.Output(_run.ClipId, 1, R2ObjectKeys.Thumbnail), _cancellation);
            Expect(thumbnail is not null, "thumbnail.jpg can't be read back");
            using MemoryStream copy = new();
            await thumbnail!.CopyToAsync(copy, _cancellation);
            Expect(copy.ToArray().AsSpan().SequenceEqual(_outputs[R2ObjectKeys.Thumbnail]), "thumbnail.jpg's bytes differ");
            return string.Join(", ", seen) + $", Cache-Control '{R2ObjectKeys.ImmutableCacheControl}'";
        });

        // A refused PUT writes nothing, so these target thumbnail_5.jpg, which is already there, and check it after.
        string refusedFile = R2ObjectKeys.Thumbnails[^1];
        string refusedUrl = urls.UploadUrls[refusedFile];
        await Check("worker.refuse-cache-control", "a PUT with another Cache-Control is refused (it is signed)", async () =>
        {
            Fetched response = await PutAsync(refusedUrl, RandomBytes(16), "image/jpeg", "no-cache");
            Expect(response.Status == HttpStatusCode.Forbidden, $"answered {response.Describe()}: an unsigned Cache-Control was accepted");
            return response.Describe();
        });
        await Check("worker.refuse-content-type", "a PUT with another Content-Type is refused (it is signed)", async () =>
        {
            Fetched response = await PutAsync(refusedUrl, RandomBytes(16), "application/octet-stream",
                R2ObjectKeys.ImmutableCacheControl);
            Expect(response.Status == HttpStatusCode.Forbidden, $"answered {response.Describe()}: an unsigned Content-Type was accepted");
            return response.Describe();
        });
        if (_outputs.ContainsKey(refusedFile))
        {
            await Check("worker.refused-unchanged", $"{refusedFile} is unchanged by the refused PUTs", async () =>
            {
                long? size = await _media.GetObjectSizeAsync(R2ObjectKeys.Output(_run.ClipId, 1, refusedFile), _cancellation);
                Expect(size == _outputs[refusedFile].Length, $"it is now {size} bytes");
                return $"{size} bytes, as the worker uploaded it";
            });
        }

        await NoteAsync("worker.chunked", "a presigned PUT without Content-Length (chunked)", async () =>
        {
            HttpRequestMessage request = new(HttpMethod.Put, refusedUrl)
            {
                Content = new StreamContent(new NonSeekableStream(_outputs.GetValueOrDefault(refusedFile) ?? RandomBytes(16)))
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            request.Headers.TryAddWithoutValidation("Cache-Control", R2ObjectKeys.ImmutableCacheControl);
            request.Headers.TransferEncodingChunked = true;
            Fetched response = await SendAsync(_raw, request);
            return response.Status == HttpStatusCode.OK
                ? "accepted here; S3 refuses chunked uploads to presigned URLs (411/501), which no caller sends: " +
                  "MediaTransfer and the browser always send Content-Length"
                : $"refused ({response.Describe()}); no caller sends one: MediaTransfer and the browser always send Content-Length";
        });
    }

    private async Task<Fetched> PutAsync(string url, byte[] body, string contentType, string cacheControl)
    {
        HttpRequestMessage request = new(HttpMethod.Put, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        request.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        return await SendAsync(_raw, request);
    }

    /// <summary>
    /// The download link: <see cref="R2Downloads.DownloadUrl"/> for a title with characters a header can't carry,
    /// cleaned by <see cref="DownloadFileName"/>, and the bucket must answer with exactly the
    /// <c>Content-Disposition</c> the URL asks for.
    /// </summary>
    private async Task DownloadAsync()
    {
        if (!_outputs.TryGetValue(R2ObjectKeys.Av1, out byte[]? av1))
        {
            SkipRest("download", "there is no av1.mp4");
            return;
        }

        await Check("download.disposition", "the download link answers with the attachment name", async () =>
        {
            string fileName = DownloadFileName.For("Storage check: Ünïcødé \"title\" 🎬 / clip.mp4", _run.ClipId);
            string url = new R2Downloads(_media).DownloadUrl(new ClipVideoRef(VideoHostNames.R2, _run.ClipId), fileName);
            HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await _raw.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                _cancellation);
            Expect(response.StatusCode == HttpStatusCode.OK, $"answered {(int)response.StatusCode}");
            string? disposition = StorageHttp.Header(response, "Content-Disposition");
            string expected = R2Downloads.AttachmentDisposition(fileName);
            Expect(disposition == expected, $"Content-Disposition is '{disposition}', expected '{expected}'");
            // The browser reads it all; the first 64 KiB are enough to know it is the right file.
            await using Stream body = await response.Content.ReadAsStreamAsync(_cancellation);
            byte[] start = new byte[64 * Kibibyte];
            int read = await body.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, _cancellation);
            Expect(start.AsSpan(0, read).SequenceEqual(av1.AsSpan(0, read)), "the bytes differ from av1.mp4");
            return $"200, Content-Disposition '{disposition}'";
        });
    }

    private async Task NoteAsync(string id, string title, Func<Task<string>> body)
    {
        _cancellation.ThrowIfCancellationRequested();
        try
        {
            Note(id, title, await body());
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Note(id, title, $"couldn't tell: {CheckReport.Describe(error)}");
        }
    }
}

/// <summary>
/// A stream that can't seek or say its length, like an HTTP response body (Twitch import, the evidence copy) or the
/// export's pipe, so <see cref="R2Storage.UploadAsync"/> takes the same path it does in production.
/// </summary>
public sealed class NonSeekableStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
