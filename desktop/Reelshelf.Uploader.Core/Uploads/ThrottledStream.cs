using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Core.Uploads;

/// <summary>
/// The upload speed limit: a token bucket refilled at the limit, holding at most a second's worth, so a read waits
/// until the bytes it took have been earned. Without a rate nothing waits.
/// </summary>
public sealed class TokenBucket(IClock clock)
{
    private readonly Lock _lock = new();
    private long? _bytesPerSecond;
    private double _tokens;
    private DateTimeOffset _last = clock.UtcNow;

    public static long? FromMbps(int? mbps) => mbps is { } value and > 0 ? value * 1_000_000L / 8 : null;

    public long? BytesPerSecond
    {
        get
        {
            lock (_lock)
            {
                return _bytesPerSecond;
            }
        }
    }

    public void SetRate(long? bytesPerSecond)
    {
        lock (_lock)
        {
            if (_bytesPerSecond == bytesPerSecond)
            {
                return;
            }

            _bytesPerSecond = bytesPerSecond is > 0 ? bytesPerSecond : null;
            _tokens = 0;
            _last = clock.UtcNow;
        }
    }

    /// <summary>Takes <paramref name="bytes"/>, waiting first when the bucket is in debt.</summary>
    public async Task TakeAsync(int bytes, CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_lock)
        {
            if (_bytesPerSecond is not { } rate)
            {
                return;
            }

            DateTimeOffset now = clock.UtcNow;
            _tokens = Math.Min(rate, _tokens + (now - _last).TotalSeconds * rate);
            _last = now;
            _tokens -= bytes;
            wait = _tokens < 0 ? TimeSpan.FromSeconds(-_tokens / rate) : TimeSpan.Zero;
        }

        if (wait > TimeSpan.Zero)
        {
            await clock.Delay(wait, cancellationToken);
        }
    }
}

/// <summary>A read-only stream that takes every byte it reads from a <see cref="TokenBucket"/>.</summary>
public sealed class ThrottledStream(Stream inner, TokenBucket bucket) : Stream
{
    /// <summary>Reads are cut to this, so the limit is felt smoothly rather than in bursts.</summary>
    private const int MaxRead = 64 * 1024;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, MaxRead)], cancellationToken);
        if (read > 0)
        {
            await bucket.TakeAsync(read, cancellationToken);
        }

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>One part's bytes of a file: <paramref name="length"/> bytes from <paramref name="offset"/>.</summary>
public sealed class FileSliceStream : Stream
{
    private readonly FileStream _file;
    private readonly long _start;
    private long _remaining;

    public FileSliceStream(string path, long offset, long length)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        _file.Position = offset;
        _start = offset;
        Length = length;
        _remaining = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _file.Position - _start;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        int read = await _file.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
        _remaining -= read;
        return read;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }

        base.Dispose(disposing);
    }
}
