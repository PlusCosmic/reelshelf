using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Games;
using Reelshelf.Moderation;
using Reelshelf.Plans;
using Reelshelf.Video;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

public sealed partial class StorageCheckRunner
{
    /// <summary>The server-side clip's objects by key, once written.</summary>
    private readonly Dictionary<string, byte[]> _serverObjects = new(StringComparer.Ordinal);

    private string? _exportKey;
    private string? _gameArtKey;
    private bool _quarantined;

    private string ServerUpload => R2ObjectKeys.Upload(_run.ServerClipId);
    private string ServerAv1 => R2ObjectKeys.Output(_run.ServerClipId, 1, R2ObjectKeys.Av1);
    private string ServerThumbnail => R2ObjectKeys.Output(_run.ServerClipId, 1, R2ObjectKeys.Thumbnail);

    /// <summary>
    /// The API's own writes and reads, each from the kind of stream it gets in production: <c>UploadAsync</c> from a
    /// body of unknown length (a Twitch download), once under a part (one PUT) and once over (a multipart upload of
    /// <see cref="R2Storage.PartSize"/> parts), and <c>PutAsync</c> with its <c>Cache-Control</c>.
    /// </summary>
    private async Task ServerSideAsync()
    {
        byte[] upload = RandomBytes(R2Storage.PartSize + 64 * Kibibyte);
        byte[] av1 = RandomBytes(256 * Kibibyte);
        byte[] thumbnail = RandomBytes(4 * Kibibyte);

        await Check("server.upload-single", "UploadAsync under a part: one PUT", async () =>
        {
            await _media.UploadAsync(ServerAv1, new NonSeekableStream(av1), R2ObjectKeys.ContentType(R2ObjectKeys.Av1),
                _cancellation);
            _serverObjects[ServerAv1] = av1;
            long? size = await _media.GetObjectSizeAsync(ServerAv1, _cancellation);
            Expect(size == av1.Length, $"stored {size} bytes of {av1.Length}");
            return $"{ServerAv1}, {Mib(av1.Length)}";
        });
        await Check("server.upload-multipart", "UploadAsync over a part: a multipart upload, as Twitch import does", async () =>
        {
            await _media.UploadAsync(ServerUpload, new NonSeekableStream(upload), R2VideoHost.SourceContentType, _cancellation);
            _serverObjects[ServerUpload] = upload;
            long? size = await _media.GetObjectSizeAsync(ServerUpload, _cancellation);
            Expect(size == upload.Length, $"stored {size} bytes of {upload.Length}");
            return $"{ServerUpload}, {Mib(upload.Length)} in two parts";
        });
        await Check("server.put", "PutAsync with Cache-Control, as the game art copy does", async () =>
        {
            await _media.PutAsync(ServerThumbnail, thumbnail, R2ObjectKeys.ContentType(R2ObjectKeys.Thumbnail),
                R2ObjectKeys.ImmutableCacheControl, _cancellation);
            _serverObjects[ServerThumbnail] = thumbnail;
            Fetched head = await GetAsync(_raw, _media.PresignGet(ServerThumbnail, TimeSpan.FromMinutes(10)), "bytes=0-0");
            Expect(head.Status == HttpStatusCode.PartialContent, $"a presigned GET of it answered {head.Describe()}");
            Expect(head["Cache-Control"] == R2ObjectKeys.ImmutableCacheControl, $"stored with Cache-Control '{head["Cache-Control"]}'");
            Expect(head["Content-Type"] == "image/jpeg", $"stored as '{head["Content-Type"]}'");
            return $"{ServerThumbnail}, image/jpeg, {R2ObjectKeys.ImmutableCacheControl}";
        });
        await Check("server.read", "OpenReadAsync and GetObjectSizeAsync read them back", async () =>
        {
            foreach ((string key, byte[] content) in _serverObjects.Where(pair => pair.Value.Length <= R2Storage.PartSize))
            {
                await using Stream? stream = await _media.OpenReadAsync(key, _cancellation);
                Expect(stream is not null, $"{key} can't be opened");
                using MemoryStream copy = new();
                await stream!.CopyToAsync(copy, _cancellation);
                Expect(copy.ToArray().AsSpan().SequenceEqual(content), $"{key}'s bytes differ");
            }

            foreach ((string key, byte[] content) in _serverObjects)
            {
                Expect(await _media.GetObjectSizeAsync(key, _cancellation) == content.Length, $"{key}'s size differs");
            }

            return $"{_serverObjects.Count} objects, same bytes and sizes";
        });
        await Check("server.list", "ListAsync lists exactly what was written", async () =>
        {
            List<(string Key, long Size)> clips = await _media.ListAsync(R2ObjectKeys.ClipPrefix(_run.ServerClipId));
            List<(string Key, long Size)> uploads = (await _media.ListAsync(ServerUpload)).Where(item => item.Key == ServerUpload).ToList();
            Dictionary<string, long> listed = clips.Concat(uploads).ToDictionary(item => item.Key, item => item.Size);
            Expect(listed.Count == _serverObjects.Count &&
                   _serverObjects.All(pair => listed.GetValueOrDefault(pair.Key, -1) == pair.Value.Length),
                $"listed {string.Join(", ", listed.Select(pair => $"{pair.Key} ({pair.Value})"))}");
            return string.Join(", ", listed.Keys);
        });
    }

    /// <summary>
    /// An export as <c>ClipExportProcessor</c> makes one, through the real <see cref="ClipExportBuilder"/>: the clip's
    /// <c>av1.mp4</c> read through <see cref="R2VideoReader"/>'s presigned GET by <see cref="ClipExportVideoSource"/>,
    /// zipped into a pipe, and the pipe streamed by <see cref="R2ExportStorage"/> into <c>UploadAsync</c>: a multipart
    /// upload from a stream of unknown length, since the zip is larger than a part. Then the part's download link.
    /// </summary>
    private async Task ExportAsync()
    {
        if (!_outputs.TryGetValue(R2ObjectKeys.Av1, out byte[]? av1))
        {
            SkipRest("export", "there is no av1.mp4 to export");
            return;
        }

        R2ExportStorage exports = new(_media);
        ClipExport export = new(_run.ExportId, _run.ExportUserId, "storage-check", "running", [_run.ClipId], [], 1, null,
            DateTimeOffset.UtcNow, null, null, _run.ExportLeaseId);
        bool built = await Check("export.build", "build an export zip (ClipExportBuilder.BuildAsync)", async () =>
        {
            ExportClipRow row = new()
            {
                Id = _run.ClipId,
                VideoId = _run.ClipId,
                Title = "Storage check",
                CreatedAt = DateTimeOffset.UtcNow,
                StorageSize = av1.Length,
                VideoState = ClipVideoStatus.Ready.ToDatabase()
            };
            // PlanSetup's clip-export client: the default handler, a 30-minute timeout.
            ClipExportVideoSource videos = new(new R2VideoReader(_media), new SingleHandlerHttpClientFactory(
                _services.StorageHandler, TimeSpan.FromMinutes(30)));
            ClipExportBuilder builder = new(videos, exports, NullLogger<ClipExportBuilder>.Instance);
            List<ClipExportPart> parts = await builder.BuildAsync(export, [row], ExportParts.DefaultMaxPartBytes,
                () => Task.CompletedTask, _cancellation);
            Expect(parts.Count == 1 && parts[0].ClipCount == 1, $"made {parts.Count} parts");
            _exportKey = ExportParts.Key(export, 1);
            long? size = await _media.GetObjectSizeAsync(_exportKey, _cancellation);
            Expect(size == parts[0].SizeBytes, $"stored {size} bytes, the zip was {parts[0].SizeBytes}");
            Expect(size > R2Storage.PartSize, "the zip fit in one part, so no multipart upload was made");
            return $"{_exportKey}, {Mib(size!.Value)} streamed through a pipe in {(size.Value + R2Storage.PartSize - 1) / R2Storage.PartSize} parts";
        });
        if (!built || _exportKey is null)
        {
            return;
        }

        await Check("export.download", "the export's download link (R2ExportStorage.DownloadUrl)", async () =>
        {
            string fileName = ExportParts.FileName(1);
            Fetched head = await GetAsync(_raw, exports.DownloadUrl(_exportKey, fileName), "bytes=0-3");
            Expect(head.Status == HttpStatusCode.PartialContent, $"answered {head.Describe()}");
            Expect(head.Body.AsSpan().SequenceEqual("PK\u0003\u0004"u8), "it doesn't start like a zip");
            string expected = R2Downloads.AttachmentDisposition(fileName);
            Expect(head["Content-Disposition"] == expected, $"Content-Disposition is '{head["Content-Disposition"]}'");
            return $"206, a zip, Content-Disposition '{expected}'";
        });
    }

    private byte[]? _gameArt;

    private async Task GameArtAsync()
    {
        await Check("gameart.put", "copy a game cover (R2GameArtStore.PutAsync)", async () =>
        {
            string? key = GameArtKeys.For(_run.GameCategoryId, GameArtKind.Cover,
                "https://images.igdb.com/igdb/image/upload/t_cover_big/storagecheck.jpg");
            Expect(key is not null, "GameArtKeys made no key");
            byte[] cover = RandomBytes(4 * Kibibyte);
            await new R2GameArtStore(_media).PutAsync(key!, cover, GameArtKeys.ContentType(key!), _cancellation);
            _gameArtKey = key;
            _gameArt = cover;
            return key!;
        });
    }

    /// <summary>
    /// The quarantine exactly as the evidence processor runs it: open uploads on the source aborted, every object of the
    /// clip copied server side (<c>CopyObject</c>) into <c>quarantine/{evidenceId}/</c>, the originals deleted, and a
    /// repeat a no-op. The copy keeps each object's <c>Content-Type</c> and <c>Cache-Control</c>.
    /// </summary>
    private async Task QuarantineAsync()
    {
        if (_serverObjects.Count == 0)
        {
            SkipRest("quarantine", "nothing was written to quarantine");
            return;
        }

        R2EvidenceQuarantine quarantine = new(_media);
        bool moved = await Check("quarantine.move", "quarantine the clip (QuarantineAsync)", async () =>
        {
            await quarantine.QuarantineAsync(_run.EvidenceId, _run.ServerClipId, _cancellation);
            _quarantined = true;
            List<(string Key, long Size)> left = await _media.ListAsync(R2ObjectKeys.ClipPrefix(_run.ServerClipId));
            Expect(left.Count == 0, $"{left.Count} originals are still under clips/");
            Expect(await _media.GetObjectSizeAsync(ServerUpload, _cancellation) is null, "the upload is still there");
            Dictionary<string, long> copies = (await _media.ListAsync(EvidenceQuarantineKeys.Prefix(_run.EvidenceId)))
                .ToDictionary(item => item.Key, item => item.Size);
            foreach ((string key, byte[] content) in _serverObjects)
            {
                string target = EvidenceQuarantineKeys.For(_run.EvidenceId, key);
                Expect(copies.GetValueOrDefault(target, -1) == content.Length, $"{target} is missing or the wrong size");
            }

            return $"{copies.Count} objects moved to {EvidenceQuarantineKeys.Prefix(_run.EvidenceId)}";
        });
        if (!moved)
        {
            return;
        }

        await Check("quarantine.headers", "the copies keep Content-Type and Cache-Control", async () =>
        {
            string key = EvidenceQuarantineKeys.For(_run.EvidenceId, ServerThumbnail);
            Fetched head = await GetAsync(_raw, _media.PresignGet(key, TimeSpan.FromMinutes(10)), "bytes=0-0");
            Expect(head.Status == HttpStatusCode.PartialContent, $"a presigned GET of the copy answered {head.Describe()}");
            Expect(head["Content-Type"] == "image/jpeg", $"the copy is '{head["Content-Type"]}'");
            Expect(head["Cache-Control"] == R2ObjectKeys.ImmutableCacheControl, $"the copy has Cache-Control '{head["Cache-Control"]}'");
            return "image/jpeg, immutable";
        });
        await Check("quarantine.open", "OpenAsync reads a copy back", async () =>
        {
            await using Stream? stream = await quarantine.OpenAsync(_run.EvidenceId, ServerThumbnail, _cancellation);
            Expect(stream is not null, "the copy can't be opened");
            using MemoryStream copy = new();
            await stream!.CopyToAsync(copy, _cancellation);
            Expect(copy.ToArray().AsSpan().SequenceEqual(_serverObjects[ServerThumbnail]), "the copy's bytes differ");
            return "same bytes";
        });
        await Check("quarantine.repeat", "quarantining again is a no-op", async () =>
        {
            await quarantine.QuarantineAsync(_run.EvidenceId, _run.ServerClipId, _cancellation);
            int count = (await _media.ListAsync(EvidenceQuarantineKeys.Prefix(_run.EvidenceId))).Count;
            Expect(count == _serverObjects.Count, $"{count} objects in the quarantine now");
            return "unchanged";
        });
    }

    /// <summary>
    /// The evidence copy as <c>ClipEvidenceProcessor</c> makes it, with the API's key on the evidence bucket, which may
    /// write, list and delete there but not read: <c>record.json</c> from a buffer, <c>video.mp4</c> streamed from the
    /// quarantined <c>av1.mp4</c>'s GetObject body (one PUT), <c>source</c> from the quarantined upload's (a multipart
    /// upload, as for a clip that never encoded), then deleting the folder as retention does.
    /// </summary>
    private async Task EvidenceAsync()
    {
        if (_evidence is null)
        {
            SkipRest("evidence", "Moderation__EvidenceBucket isn't set");
            return;
        }

        R2EvidenceStorage evidence = new(_evidence);
        R2EvidenceQuarantine quarantine = new(_media);
        string prefix = ClipEvidenceProcessor.Prefix(_run.EvidenceId);
        Dictionary<string, long> written = [];
        byte[] record = Encoding.UTF8.GetBytes(
            $"{{\"storage_check\":true,\"evidence_id\":\"{_run.EvidenceId}\",\"clip_id\":\"{_run.ServerClipId}\"}}");

        await Check("evidence.record", "write record.json (UploadAsync, one PUT)", async () =>
        {
            string key = prefix + ClipEvidenceProcessor.RecordFile;
            await evidence.UploadAsync(key, new MemoryStream(record, writable: false), "application/json", _cancellation);
            written[key] = record.Length;
            return key;
        });
        if (_quarantined)
        {
            await Check("evidence.video", "copy video.mp4 from the quarantine (a GetObject body into one PUT)", async () =>
            {
                await using Stream? av1 = await quarantine.OpenAsync(_run.EvidenceId, ServerAv1, _cancellation);
                Expect(av1 is not null, "the quarantined av1.mp4 can't be opened");
                string key = prefix + ClipEvidenceProcessor.VideoFile;
                await evidence.UploadAsync(key, av1!, "video/mp4", _cancellation);
                written[key] = _serverObjects[ServerAv1].Length;
                return key;
            });
            await Check("evidence.source", "copy source from the quarantine (a GetObject body into a multipart upload)", async () =>
            {
                await using Stream? source = await quarantine.OpenAsync(_run.EvidenceId, ServerUpload, _cancellation);
                Expect(source is not null, "the quarantined upload can't be opened");
                string key = prefix + ClipEvidenceProcessor.SourceFile;
                await evidence.UploadAsync(key, source!, "application/octet-stream", _cancellation);
                written[key] = _serverObjects[ServerUpload].Length;
                return $"{key}, {Mib(written[key])} in two parts";
            });
        }
        else
        {
            Skip("evidence.video", "copy the video from the quarantine", "nothing was quarantined");
        }

        await Check("evidence.list", "the evidence folder lists what was written", async () =>
        {
            Dictionary<string, long> listed = (await _evidence.ListAsync(prefix)).ToDictionary(item => item.Key, item => item.Size);
            Expect(written.All(pair => listed.GetValueOrDefault(pair.Key, -1) == pair.Value) && listed.Count == written.Count,
                $"listed {string.Join(", ", listed.Select(pair => $"{pair.Key} ({pair.Value})"))}");
            return $"{listed.Count} objects, sizes match";
        });
        await NoteAsync("evidence.read", "whether the API's key can read evidence", async () =>
        {
            try
            {
                await using Stream? stream = await _evidence.OpenReadAsync(prefix + ClipEvidenceProcessor.RecordFile, _cancellation);
                return stream is null
                    ? "the record reads as missing"
                    : "it CAN (aws/storage/main.tf grants no s3:GetObject there; the API never reads evidence)";
            }
            catch (Amazon.S3.AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.Forbidden)
            {
                return $"it can't ({error.ErrorCode}), as aws/storage/main.tf intends; nothing in the API reads evidence";
            }
        });
        await Check("evidence.delete", "delete the folder as retention does (DeletePrefixAsync)", async () =>
        {
            await evidence.DeletePrefixAsync(prefix);
            List<(string Key, long Size)> left = await _evidence.ListAsync(prefix);
            Expect(left.Count == 0, $"{left.Count} objects left");
            return "listed again: empty";
        });
    }

    /// <summary>
    /// An upload the browser started and never finished, with a part already stored: deleting the clip aborts it with
    /// <see cref="R2Storage.AbortMultipartUploadsAsync"/>, which finds it by <c>ListMultipartUploads</c> on its key.
    /// </summary>
    private async Task AbandonedUploadAsync()
    {
        string key = R2ObjectKeys.Upload(_run.AbandonedClipId);
        string? uploadId = null;
        bool opened = await Check("abandoned.open", "start an upload and store one part", async () =>
        {
            uploadId = await _media.StartMultipartUploadAsync(key, R2VideoHost.SourceContentType, _cancellation);
            string url = _media.PresignUploadPart(key, uploadId, 1, R2VideoHost.PartUrlLifetime);
            Fetched put = await SendAsync(_raw, new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new ByteArrayContent(RandomBytes(64 * Kibibyte))
            });
            Expect(put.Status == HttpStatusCode.OK, $"the part PUT answered {put.Describe()}");
            List<UploadedPart>? parts = await _media.ListPartsAsync(key, uploadId, _cancellation);
            Expect(parts?.Count == 1, "the part isn't listed");
            return $"upload on {key} with one 64 KiB part";
        });
        if (!opened || uploadId is null)
        {
            if (uploadId is not null)
            {
                await _media.AbortMultipartUploadsAsync(key);
            }

            return;
        }

        await Check("abandoned.abort", "AbortMultipartUploadsAsync finds and aborts it", async () =>
        {
            await _media.AbortMultipartUploadsAsync(key);
            Expect(await _media.ListPartsAsync(key, uploadId, _cancellation) is null, "the upload is still open");
            Amazon.S3.Model.ListMultipartUploadsResponse open = await _mediaClient.ListMultipartUploadsAsync(
                new Amazon.S3.Model.ListMultipartUploadsRequest { BucketName = _settings.Store.Bucket, Prefix = key },
                _cancellation);
            int count = (open.MultipartUploads ?? []).Count(upload => upload.Key == key);
            Expect(count == 0, $"{count} uploads still open on {key}");
            await _media.AbortMultipartUploadsAsync(key);
            return "aborted; none open on the key; aborting again is a no-op";
        });
    }
}
