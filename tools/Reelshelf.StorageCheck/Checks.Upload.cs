using System.Net;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Reelshelf.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace Reelshelf.StorageCheck;

public sealed partial class StorageCheckRunner
{
    /// <summary>The browser's source, once it is in the bucket.</summary>
    private byte[]? _source;

    private async Task ChecksAsync()
    {
        _report.Heading("Missing keys (before anything is written)");
        await MissingKeysAsync();

        _report.Heading("Browser upload (frontend/src/utils/s3MultipartUpload.ts, R2VideoHost, MultipartUploadEndpoints)");
        await BrowserUploadAsync();

        _report.Heading("Upload probe (R2VideoHost.CompleteUploadAsync, FfprobeUploadProbe)");
        await ProbeAsync();

        _report.Heading("Worker (R2Storage.UrlsFor, the worker's MediaTransfer)");
        await WorkerAsync();

        _report.Heading("Download (R2Downloads, DownloadFileName)");
        await DownloadAsync();

        _report.Heading("Server side (R2Storage: UploadAsync, PutAsync, OpenReadAsync, ListAsync)");
        await ServerSideAsync();

        _report.Heading("Export (ClipExportBuilder through a pipe, ClipExportVideoSource, R2ExportStorage)");
        await ExportAsync();

        _report.Heading("Game art (R2GameArtStore)");
        await GameArtAsync();

        _report.Heading("Quarantine (R2EvidenceQuarantine)");
        await QuarantineAsync();

        _report.Heading("Evidence bucket (R2EvidenceStorage, as ClipEvidenceProcessor uses it)");
        await EvidenceAsync();

        _report.Heading("Abandoned upload (R2Storage.AbortMultipartUploadsAsync, as deleting a clip does)");
        await AbandonedUploadAsync();

        _report.Heading("CDN (Storage:MediaBaseUrl)");
        await CdnAsync();

        _report.Heading("Purge (CloudFrontCdnPurger)");
        await PurgeAsync();

        _report.Heading("Deletes (R2EvidenceQuarantine.DeleteAsync, R2GameArtStore.DeleteAsync, R2VideoHost.DeleteVideoAsync's calls)");
        await DeletesAsync();
    }

    /// <summary>
    /// Every read the API makes of a key that may not exist must see "not there" (null, false, a worker 404) rather than
    /// an access error, which on S3 depends on the key holding <c>s3:ListBucket</c>: without it S3 answers 403.
    /// </summary>
    private async Task MissingKeysAsync()
    {
        Guid missing = _run.AbandonedClipId;
        await Check("missing.size", "GetObjectSizeAsync of a missing key is null", async () =>
        {
            long? size = await _media.GetObjectSizeAsync(R2ObjectKeys.Upload(missing), _cancellation);
            Expect(size is null, $"answered {size} bytes for a key nothing wrote");
            return "null, as R2VideoHost's completion and the quarantine expect";
        });
        await Check("missing.read", "OpenReadAsync of a missing key is null", async () =>
        {
            await using Stream? stream = await _media.OpenReadAsync(
                R2ObjectKeys.Output(missing, 1, R2ObjectKeys.Av1), _cancellation);
            Expect(stream is null, "opened a key nothing wrote");
            return "null";
        });
        await Check("missing.list", "ListAsync of an empty prefix is empty", async () =>
        {
            List<(string Key, long Size)> found = await _media.ListAsync(R2ObjectKeys.ClipPrefix(missing));
            Expect(found.Count == 0, $"listed {found.Count} objects under a fresh prefix");
            return "no objects";
        });
        await Check("missing.parts", "ListPartsAsync of an unknown upload is null", async () =>
        {
            List<UploadedPart>? parts = await _media.ListPartsAsync(R2ObjectKeys.Upload(missing), "storage-check-no-such-upload",
                _cancellation);
            Expect(parts is null, "listed parts of an upload that doesn't exist");
            return "null (NoSuchUpload), which the parts endpoint turns into 404 so a browser forgets the upload";
        });
        await Check("missing.worker", "the worker sees a missing source as 404, not retryable", async () =>
        {
            string url = _media.PresignGet(R2ObjectKeys.Upload(missing), TimeSpan.FromMinutes(10));
            MediaTransfer transfer = new(WorkerHttp(), NullLogger<MediaTransfer>.Instance);
            try
            {
                await transfer.DownloadAsync(url, Path.Combine(_workDirectory, "missing"), long.MaxValue, _cancellation);
            }
            catch (JobFailedException error) when (!error.Retryable && error.Message.Contains("404", StringComparison.Ordinal))
            {
                return error.Message;
            }

            throw new CheckFailedException("the download of a missing key didn't fail as a non-retryable 404");
        });
    }

    /// <summary>
    /// The browser's upload, as <c>s3MultipartUpload.ts</c> does it: the API starts the upload and presigns each part
    /// (signing only the host); the browser PUTs each part's bytes with no <c>Content-Type</c> (a <c>Blob</c> sliced
    /// without one), so its CORS preflight asks for no headers, and reads the <c>ETag</c>, which the bucket's CORS must
    /// expose. The desktop uploader PUTs the same URLs with <c>Content-Length</c> and nothing else, and no
    /// <c>Origin</c>.
    /// </summary>
    private async Task BrowserUploadAsync()
    {
        string key = R2ObjectKeys.Upload(_run.ClipId);
        byte[] source = RandomBytes(SourceSize);
        MultipartUploadInfo? info = null;
        bool started = await Check("browser.start", "start the multipart upload (CreateUploadTargetAsync)", async () =>
        {
            string uploadId = await _media.StartMultipartUploadAsync(key, R2VideoHost.SourceContentType, _cancellation);
            info = MultipartUploadParts.For(uploadId, source.Length);
            Expect(info.PartCount == 2, $"cut into {info.PartCount} parts, not 2");
            return $"upload on {key}: {info.PartCount} parts of {Mib(info.PartSize)} ({Mib(source.Length)} in all)";
        });
        if (!started || info is null)
        {
            SkipRest("browser", "the upload didn't start");
            return;
        }

        Dictionary<int, string> urls = [];
        await Check("browser.presign", "presign the part URLs (PresignParts)", () =>
        {
            for (int part = 1; part <= info.PartCount; part++)
            {
                urls[part] = _media.PresignUploadPart(key, info.UploadId, part, R2VideoHost.PartUrlLifetime);
            }

            return Task.FromResult($"{urls.Count} URLs, {R2VideoHost.PartUrlLifetime.TotalMinutes:0} minutes each");
        });

        await Check("browser.preflight", "CORS preflight for a part PUT from the site is allowed", async () =>
        {
            Fetched preflight = await PreflightAsync(urls[1], _settings.SiteOrigin, "PUT", null);
            Expect(preflight.Status == HttpStatusCode.OK, $"answered {preflight.Describe()}");
            Expect(string.Equals(preflight["Access-Control-Allow-Origin"], _settings.SiteOrigin, StringComparison.Ordinal),
                $"Access-Control-Allow-Origin is '{preflight["Access-Control-Allow-Origin"]}'");
            Expect(preflight["Access-Control-Allow-Methods"]?.Contains("PUT", StringComparison.OrdinalIgnoreCase) == true,
                $"Access-Control-Allow-Methods is '{preflight["Access-Control-Allow-Methods"]}'");
            return $"200, allows {preflight["Access-Control-Allow-Methods"]} for {_settings.SiteOrigin}";
        });
        await Check("browser.preflight-other", "CORS preflight from another origin is refused", async () =>
        {
            Fetched preflight = await PreflightAsync(urls[1], _settings.OtherOrigin, "PUT", null);
            // A browser fails a preflight that isn't 2xx, or that doesn't allow its origin, whatever else it says.
            string? allowed = preflight["Access-Control-Allow-Origin"];
            bool allows = allowed == "*" || allowed == _settings.OtherOrigin;
            Expect((int)preflight.Status is < 200 or >= 300 || !allows, $"answered {preflight.Describe()} allowing '{allowed}'");
            return allows
                ? $"{preflight.Describe()}, which a browser treats as refused"
                : $"{preflight.Describe()}, no Access-Control-Allow-Origin for {_settings.OtherOrigin}";
        });
        await NoteAsync("browser.preflight-content-type", "preflight asking for Content-Type", async () =>
        {
            Fetched withType = await PreflightAsync(urls[1], _settings.SiteOrigin, "PUT", "content-type");
            return withType.Status == HttpStatusCode.OK && withType["Access-Control-Allow-Origin"] == _settings.SiteOrigin
                ? "allowed (the bucket allows content-type; the uploader sends none on parts today)"
                : $"refused ({withType.Describe()}); harmless while the uploader sends no Content-Type on parts";
        });

        Dictionary<int, string> etags = [];
        bool uploaded = true;
        for (int part = 1; part <= info.PartCount; part++)
        {
            int number = part;
            uploaded &= await Check($"browser.put-part-{number}", $"PUT part {number} as the browser does", async () =>
            {
                (long start, long length) = PartRange(info, number, source.Length);
                HttpRequestMessage request = new(HttpMethod.Put, urls[number])
                {
                    Content = new ByteArrayContent(source, (int)start, (int)length)
                };
                request.Headers.TryAddWithoutValidation("Origin", _settings.SiteOrigin);
                request.Headers.TryAddWithoutValidation("Accept", "*/*");
                Fetched response = await SendAsync(_raw, request);
                Expect(response.Status == HttpStatusCode.OK, $"answered {response.Describe()}");
                string? etag = response["ETag"];
                Expect(!string.IsNullOrEmpty(etag), "no ETag in the response");
                etags[number] = etag!;
                Expect(response["Access-Control-Allow-Origin"] == _settings.SiteOrigin,
                    $"Access-Control-Allow-Origin is '{response["Access-Control-Allow-Origin"]}', so the browser can't read the response");
                string? exposed = response["Access-Control-Expose-Headers"];
                Expect(exposed is not null && exposed.Split(',').Any(name => name.Trim().Equals("ETag", StringComparison.OrdinalIgnoreCase)),
                    $"Access-Control-Expose-Headers is '{exposed}', so the browser can't read the ETag and can't complete");
                return $"200, {Mib(length)}, ETag {etag} exposed to {_settings.SiteOrigin}";
            });
        }

        if (!uploaded)
        {
            SkipRest("browser", "a part didn't upload");
            await FallBackToServerUploadAsync(key, source);
            return;
        }

        await Check("browser.put-part-desktop", "PUT part 2 again as the desktop uploader does", async () =>
        {
            (long start, long length) = PartRange(info, 2, source.Length);
            // reqwest with a body and an explicit Content-Length, nothing else: no Origin, no Content-Type.
            HttpRequestMessage request = new(HttpMethod.Put, urls[2])
            {
                Content = new ByteArrayContent(source, (int)start, (int)length)
            };
            request.Content.Headers.ContentLength = length;
            Fetched response = await SendAsync(_raw, request);
            Expect(response.Status == HttpStatusCode.OK, $"answered {response.Describe()}");
            Expect(response["ETag"] == etags[2], $"ETag {response["ETag"]} differs from the browser's {etags[2]} for the same bytes");
            return $"200, same ETag";
        });

        await Check("browser.list-parts", "list the parts, as a resuming browser does (ListPartsAsync)", async () =>
        {
            List<UploadedPart>? parts = await _media.ListPartsAsync(key, info.UploadId, _cancellation);
            Expect(parts is not null, "the upload is gone");
            Expect(parts!.Count == info.PartCount, $"{parts.Count} parts listed");
            foreach (UploadedPart part in parts)
            {
                (_, long length) = PartRange(info, part.PartNumber, source.Length);
                Expect(part.Etag == etags[part.PartNumber], $"part {part.PartNumber}'s ETag is {part.Etag}, the PUT said {etags[part.PartNumber]}");
                Expect(part.Size == length, $"part {part.PartNumber} is {part.Size} bytes, not {length}");
            }

            return string.Join(", ", parts.Select(part => $"part {part.PartNumber} {part.Size} bytes"));
        });

        bool completed = await Check("browser.complete", "complete it with the ETags (CompleteMultipartUploadAsync)", async () =>
        {
            List<CompletedUploadPart> parts = etags.OrderBy(pair => pair.Key)
                .Select(pair => new CompletedUploadPart(pair.Key, pair.Value)).ToList();
            bool joined = await _media.CompleteMultipartUploadAsync(key, info.UploadId, parts, _cancellation);
            Expect(joined, "the upload was gone before it was completed");
            long? size = await _media.GetObjectSizeAsync(key, _cancellation);
            Expect(size is not null, "no object after completing");
            Expect(UploadValidation.SizeProblem(source.Length, size!.Value) is null,
                UploadValidation.SizeProblem(source.Length, size.Value) ?? "");
            return $"joined; GetObjectSizeAsync says {size} bytes, exactly the declared size";
        });
        if (!completed)
        {
            SkipRest("browser", "the upload didn't complete");
            await FallBackToServerUploadAsync(key, source);
            return;
        }

        _source = source;
        await Check("browser.complete-again", "completing again (a retry after the probe's 503) doesn't throw", async () =>
        {
            List<CompletedUploadPart> parts = etags.OrderBy(pair => pair.Key)
                .Select(pair => new CompletedUploadPart(pair.Key, pair.Value)).ToList();
            bool again = await _media.CompleteMultipartUploadAsync(key, info.UploadId, parts, _cancellation);
            long? size = await _media.GetObjectSizeAsync(key, _cancellation);
            Expect(size == source.Length, $"the object is now {size} bytes");
            return again
                ? "S3 accepted the repeat; the object is unchanged"
                : "NoSuchUpload (false); R2VideoHost goes on to the joined object";
        });
    }

    /// <summary>
    /// When the browser's path fails, the source goes in through the API's own upload instead, so the probe, worker,
    /// CDN and purge checks still run and one run shows everything that is wrong.
    /// </summary>
    private async Task FallBackToServerUploadAsync(string key, byte[] source)
    {
        await Check("browser.fallback", "put the source in server side instead, for the checks after this", async () =>
        {
            await _media.AbortMultipartUploadsAsync(key);
            await _media.UploadAsync(key, new NonSeekableStream(source), R2VideoHost.SourceContentType, _cancellation);
            Expect(await _media.GetObjectSizeAsync(key, _cancellation) == source.Length, "the source isn't there");
            _source = source;
            return $"{key} uploaded with UploadAsync; the browser path still fails";
        });
    }

    private async Task<Fetched> PreflightAsync(string url, string origin, string method, string? requestHeaders)
    {
        HttpRequestMessage request = new(HttpMethod.Options, url);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        if (requestHeaders is not null)
        {
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", requestHeaders);
        }

        return await SendAsync(_raw, request);
    }

    private static (long Start, long Length) PartRange(MultipartUploadInfo info, int part, long size)
    {
        long start = (part - 1) * info.PartSize;
        return (start, Math.Min(info.PartSize, size - start));
    }

    /// <summary>
    /// The probe's reads: ffprobe opens the presigned GET with an open-ended <c>Range</c> from the start, then seeks
    /// (another open-ended range) to wherever the file's index is, often the end. Two such reads stand in for it, and
    /// the API's own probe runs too when ffprobe is on PATH.
    /// </summary>
    private async Task ProbeAsync()
    {
        if (_source is not { } source)
        {
            SkipRest("probe", "there is no uploaded source");
            return;
        }

        string url = _media.PresignGet(R2ObjectKeys.Upload(_run.ClipId), TimeSpan.FromMinutes(10));
        await Check("probe.range-start", "ranged read from the start, as ffprobe opens it", async () =>
        {
            Fetched first = await RangeAsync(url, 0, 64 * Kibibyte);
            Expect(first.Status == HttpStatusCode.PartialContent, $"answered {first.Describe()}");
            Expect(first.Body.AsSpan().SequenceEqual(source.AsSpan(0, first.Body.Length)), "the bytes differ from the upload");
            return $"206, {first["Content-Range"]}, first {first.Body.Length} bytes match";
        });
        await Check("probe.range-end", "ranged read near the end, as ffprobe seeks", async () =>
        {
            long from = source.Length - 4 * Kibibyte;
            Fetched last = await GetAsync(_raw, url, $"bytes={from}-");
            Expect(last.Status == HttpStatusCode.PartialContent, $"answered {last.Describe()}");
            Expect(last.Body.AsSpan().SequenceEqual(source.AsSpan((int)from)), "the bytes differ from the upload");
            return $"206, {last["Content-Range"]}";
        });

        if (_services.Probe is { } probe)
        {
            await Check("probe.ffprobe", "the API's FfprobeUploadProbe reads the upload over HTTPS", async () =>
            {
                try
                {
                    UploadProbeResult result = await probe.ProbeAsync(new Uri(url), _cancellation);
                    return $"read it (video {result.HasVideo}, {result.Width}x{result.Height})";
                }
                catch (UnreadableUploadException)
                {
                    return "ffprobe read the file from storage and found no video in the random test bytes, as expected " +
                           "(a storage read failure would have been a TimeoutException, the API's 503)";
                }
                catch (TimeoutException error)
                {
                    throw new CheckFailedException($"ffprobe couldn't read the upload from storage: {CheckReport.Describe(error)}");
                }
            });
        }
        else
        {
            Skip("probe.ffprobe", "the API's FfprobeUploadProbe reads the upload", _services.ProbeSkipReason);
        }

        await Check("probe.cors-get", "GET CORS follows the bucket's rule", async () =>
        {
            Fetched site = await GetAsync(_raw, url, "bytes=0-0", _settings.SiteOrigin);
            Fetched other = await GetAsync(_raw, url, "bytes=0-0", _settings.OtherOrigin);
            Expect(site.Status == HttpStatusCode.PartialContent, $"answered {site.Describe()}");
            Expect(site["Access-Control-Allow-Origin"] == _settings.SiteOrigin,
                $"Access-Control-Allow-Origin for the site is '{site["Access-Control-Allow-Origin"]}'");
            string? otherAllowed = other["Access-Control-Allow-Origin"];
            Expect(otherAllowed is null || (otherAllowed != "*" && otherAllowed != _settings.OtherOrigin),
                $"another origin is allowed ('{otherAllowed}')");
            return $"{_settings.SiteOrigin} allowed, {_settings.OtherOrigin} not";
        });
    }

    private Task<Fetched> RangeAsync(string url, long from, int length) =>
        GetAsync(_raw, url, $"bytes={from}-{from + length - 1}");

    private void SkipRest(string section, string reason) =>
        Skip($"{section}.*", "the rest of this section", reason);

    private SingleHandlerHttpClientFactory WorkerHttp() =>
        // WorkerSetup's "media" client: the default handler, a one-hour timeout.
        new(_services.StorageHandler, TimeSpan.FromHours(1));
}
