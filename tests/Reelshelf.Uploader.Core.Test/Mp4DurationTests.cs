using System.Buffers.Binary;
using System.Text;
using Reelshelf.Uploader.Core.Media;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>Reading a clip's length from the <c>mvhd</c> box of MP4 and QuickTime files.</summary>
public class Mp4DurationTests
{
    [Fact]
    public void AnMp4_WithItsMovieBoxFirst()
    {
        // NVIDIA's app writes ftyp, then moov (faststart), then mdat.
        byte[] file = Concat(
            Box("ftyp", Ascii("isom"), U32(512), Ascii("isomiso2avc1mp41")),
            Box("moov", MovieHeader(version: 0, timescale: 1000, duration: 41_250), Box("trak", new byte[24])),
            Box("mdat", new byte[4096]));

        Assert.Equal(41.25, Mp4Duration.TryRead(new MemoryStream(file)));
    }

    [Fact]
    public void AQuickTimeMov_WithAWideAtomAndTheMovieBoxAfterAHugeMdat()
    {
        // OBS's .mov: ftyp "qt  ", a wide placeholder, an mdat with a 64-bit size, and moov at the end.
        byte[] mdatBody = new byte[3000];
        byte[] mdat = Concat(U32(1), Ascii("mdat"), U64(16 + (ulong)mdatBody.Length), mdatBody);
        byte[] file = Concat(
            Box("ftyp", Ascii("qt  "), U32(0x20050300), Ascii("qt  ")),
            Box("wide", []),
            mdat,
            Box("moov", Box("udta", new byte[12]), MovieHeader(version: 0, timescale: 600, duration: 80_400)));

        Assert.Equal(134, Mp4Duration.TryRead(new MemoryStream(file)));
    }

    [Fact]
    public void AVersionOneMovieHeader_With64BitTimes()
    {
        byte[] file = Concat(
            Box("ftyp", Ascii("mp42"), U32(0)),
            Box("moov", MovieHeader(version: 1, timescale: 90_000, duration: 5_400_000)));

        Assert.Equal(60, Mp4Duration.TryRead(new MemoryStream(file)));
    }

    [Fact]
    public void AReadsFromAFileOnDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, Concat(Box("ftyp", Ascii("isom")),
            Box("moov", MovieHeader(version: 0, timescale: 30, duration: 900))));
        try
        {
            Assert.Equal(30, Mp4Duration.TryRead(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileWithNoMovieBox_OrARecordingStillBeingWritten_HasNoLength()
    {
        Assert.Null(Mp4Duration.TryRead(new MemoryStream(Concat(Box("ftyp", Ascii("isom")), Box("mdat", new byte[64])))));
        Assert.Null(Mp4Duration.TryRead(new MemoryStream(Concat(Box("ftyp", Ascii("isom")),
            Box("moov", MovieHeader(version: 0, timescale: 1000, duration: 0))))));
        Assert.Null(Mp4Duration.TryRead(new MemoryStream(Encoding.ASCII.GetBytes("not a video at all"))));
        // A box claiming more bytes than the file has.
        Assert.Null(Mp4Duration.TryRead(new MemoryStream(Concat(U32(9999), Ascii("moov"), new byte[8]))));
    }

    private static byte[] MovieHeader(byte version, uint timescale, ulong duration) => version == 1
        ? Box("mvhd", [1, 0, 0, 0], U64(0), U64(0), U32(timescale), U64(duration), new byte[80])
        : Box("mvhd", [0, 0, 0, 0], U32(0), U32(0), U32(timescale), U32((uint)duration), new byte[80]);

    private static byte[] Box(string type, params byte[][] body)
    {
        byte[] content = Concat(body);
        return Concat(U32((uint)(8 + content.Length)), Ascii(type), content);
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] U32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U64(ulong value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
}
