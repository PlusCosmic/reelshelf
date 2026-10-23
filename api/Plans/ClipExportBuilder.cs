using System.IO.Compression;
using System.IO.Pipelines;

namespace Reelshelf.Plans;

/// <summary>A clip's video for an export, and whatever must be cleaned up once it has been copied.</summary>
public sealed class ExportVideo(Stream content, Action? cleanup = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        cleanup?.Invoke();
    }
}

/// <summary>Opens a clip's current video, or null when it has none to export (it never finished encoding).</summary>
public interface IExportVideoSource
{
    Task<ExportVideo?> OpenAsync(ExportClipRow clip, CancellationToken cancellationToken);
}

/// <summary>Where export zips go: R2, under <c>exports/</c>.</summary>
public interface IExportStorage
{
    bool IsConfigured { get; }

    /// <summary>Streams <paramref name="content"/> into <paramref name="key"/>, of a length not known up front.</summary>
    Task UploadAsync(string key, Stream content, CancellationToken cancellationToken);

    Task DeletePrefixAsync(string prefix);

    /// <summary>A short-lived download of <paramref name="key"/>, saved as <paramref name="fileName"/>.</summary>
    string DownloadUrl(string key, string fileName);
}

/// <summary>
/// Makes an export's zips: each part is written as it is uploaded, through a pipe, so no part is ever held in memory
/// or on disk whole. Clips are stored, not compressed, since video doesn't compress. A clip that has gone, or has no
/// video, is left out.
/// </summary>
public sealed class ClipExportBuilder(IExportVideoSource videos, IExportStorage storage, ILogger<ClipExportBuilder> logger)
{
    public async Task<List<ClipExportPart>> BuildAsync(ClipExport export, IReadOnlyList<ExportClipRow> clips,
        long maxPartBytes, Func<Task> heartbeat, CancellationToken cancellationToken)
    {
        HashSet<string> taken = [];
        List<ClipExportPart> parts = [];
        foreach (List<ExportClipRow> partClips in ExportParts.Plan(clips, maxPartBytes))
        {
            int index = parts.Count + 1;
            string key = ExportParts.Key(export.UserId, export.Id, index);
            Pipe pipe = new();
            CountingStream counted = new(pipe.Reader.AsStream());
            using CancellationTokenSource partCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // An upload that fails stops the zip too, which would otherwise wait for ever on a pipe nobody reads.
            Task upload = Task.Run(async () =>
            {
                try
                {
                    await storage.UploadAsync(key, counted, partCancellation.Token);
                }
                catch
                {
                    await partCancellation.CancelAsync();
                    await pipe.Reader.CompleteAsync();
                    throw;
                }
            }, CancellationToken.None);
            int written;
            try
            {
                written = await WriteZipAsync(pipe.Writer.AsStream(), partClips, taken, heartbeat,
                    partCancellation.Token);
            }
            catch (Exception error)
            {
                await pipe.Writer.CompleteAsync(error);
                try
                {
                    await upload;
                }
                catch (Exception uploadError)
                {
                    // The upload's own failure is the one worth reporting when it is what stopped the zip.
                    if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    logger.LogDebug(uploadError, "The upload of {Key} stopped with its zip", key);
                }

                throw;
            }

            await upload;
            if (written == 0)
            {
                // Every clip of this part had gone; its empty zip isn't worth a download.
                await storage.DeletePrefixAsync(key);
                continue;
            }

            parts.Add(new ClipExportPart(index, counted.BytesRead, written));
            logger.LogInformation("Export {ExportId}: part {Index} holds {Count} clips, {Bytes} bytes",
                export.Id, index, written, counted.BytesRead);
        }

        // A part left out frees its number, so the next part takes it: parts are always 1..n.
        return parts;
    }

    private async Task<int> WriteZipAsync(Stream output, IReadOnlyList<ExportClipRow> clips, ISet<string> taken,
        Func<Task> heartbeat, CancellationToken cancellationToken)
    {
        int written = 0;
        await using (output)
        {
            using ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (ExportClipRow clip in clips)
            {
                await using ExportVideo? video = await videos.OpenAsync(clip, cancellationToken);
                if (video is null)
                {
                    continue;
                }

                ZipArchiveEntry entry = zip.CreateEntry(ExportParts.EntryName(clip, taken), CompressionLevel.NoCompression);
                entry.LastWriteTime = clip.CreatedAt;
                await using (Stream target = entry.Open())
                {
                    await video.Content.CopyToAsync(target, 1024 * 1024, cancellationToken);
                }

                written++;
                await heartbeat();
            }
        }

        return written;
    }

    /// <summary>Counts what the upload reads, which is the zip's size.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
