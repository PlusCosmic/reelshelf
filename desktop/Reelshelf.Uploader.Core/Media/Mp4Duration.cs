using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Reelshelf.Uploader.Core.Media;

/// <summary>
/// Reads a clip's length from its MP4 or QuickTime (MOV) header without ffprobe: the <c>mvhd</c> box inside the
/// top-level <c>moov</c> box holds the movie's timescale and duration. Boxes before <c>moov</c> (often a huge
/// <c>mdat</c>) are skipped by seeking, so only a few kilobytes are read.
/// </summary>
public static class Mp4Duration
{
    /// <summary>The length in seconds, or null when the file has no readable <c>mvhd</c>.</summary>
    public static double? TryRead(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                FileOptions.RandomAccess);
            return TryRead(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static double? TryRead(Stream stream)
    {
        long end = stream.Length;
        long position = 0;
        while (position + 8 <= end)
        {
            if (ReadBoxHeader(stream, position, end) is not var (type, headerSize, size))
            {
                return null;
            }

            if (type == "moov")
            {
                return ReadMovie(stream, position + headerSize, position + size);
            }

            position += size;
        }

        return null;
    }

    private static double? ReadMovie(Stream stream, long start, long end)
    {
        long position = start;
        while (position + 8 <= end)
        {
            if (ReadBoxHeader(stream, position, end) is not var (type, headerSize, size))
            {
                return null;
            }

            if (type == "mvhd")
            {
                return ReadMovieHeader(stream, position + headerSize, position + size);
            }

            position += size;
        }

        return null;
    }

    /// <summary>
    /// <c>mvhd</c>: a version byte and three flag bytes, then (version 0) 32-bit creation and modification times,
    /// timescale and duration, or (version 1) 64-bit times, a 32-bit timescale and a 64-bit duration.
    /// </summary>
    private static double? ReadMovieHeader(Stream stream, long start, long end)
    {
        Span<byte> header = stackalloc byte[32];
        int available = (int)Math.Min(header.Length, end - start);
        stream.Position = start;
        if (available < 20 || stream.ReadAtLeast(header[..available], available, throwOnEndOfStream: false) < available)
        {
            return null;
        }

        byte version = header[0];
        uint timescale;
        ulong duration;
        if (version == 1)
        {
            if (available < 32)
            {
                return null;
            }

            timescale = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(header[24..]);
        }
        else
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
        }

        // An unknown duration is all ones; a recorder still writing may leave it at zero.
        bool unknown = version == 1 ? duration == ulong.MaxValue : duration == uint.MaxValue;
        if (timescale == 0 || duration == 0 || unknown)
        {
            return null;
        }

        return duration / (double)timescale;
    }

    /// <summary>A box's type, header length and whole length, or null when it runs past the end.</summary>
    private static (string Type, int HeaderSize, long Size)? ReadBoxHeader(Stream stream, long position, long end)
    {
        Span<byte> header = stackalloc byte[16];
        stream.Position = position;
        if (stream.ReadAtLeast(header[..8], 8, throwOnEndOfStream: false) < 8)
        {
            return null;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        string type = System.Text.Encoding.ASCII.GetString(header[4..8]);
        int headerSize = 8;
        if (size == 1)
        {
            // A 64-bit size follows the type.
            if (stream.ReadAtLeast(header[8..16], 8, throwOnEndOfStream: false) < 8)
            {
                return null;
            }

            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            headerSize = 16;
        }
        else if (size == 0)
        {
            // The box runs to the end of its container.
            size = end - position;
        }

        return size < headerSize || position + size > end ? null : (type, headerSize, size);
    }
}

/// <summary>A file's MD5, streamed, as lowercase hex: the form the website sends and the API compares.</summary>
public static class FileHashes
{
    public static async Task<string> Md5Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        byte[] hash = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
