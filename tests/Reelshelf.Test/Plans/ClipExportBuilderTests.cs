using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class ClipExportBuilderTests
{
    private static readonly ClipExport Export = new(Guid.NewGuid(), Guid.NewGuid(), ClipExportReasons.Requested,
        ClipExportStatuses.Running, [], [], 1, null, DateTimeOffset.UtcNow, null, null);

    private static ExportClipRow Clip(string title, int size) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        GameName = "Apex Legends",
        CreatedAt = DateTimeOffset.Parse("2026-10-23T18:00:00Z"),
        StorageSize = size
    };

    [Fact]
    public async Task EachPart_IsAZipOfItsClips_UploadedUnderTheExport()
    {
        FakeVideos videos = new();
        FakeStorage storage = new();
        ExportClipRow a = Clip("one", 300_000), b = Clip("two", 300_000), c = Clip("three", 300_000);
        foreach (ExportClipRow clip in new[] { a, b, c })
        {
            videos.Bytes[clip.Id] = Enumerable.Range(0, (int)clip.StorageSize!.Value).Select(i => (byte)i).ToArray();
        }

        int heartbeats = 0;
        List<ClipExportPart> parts = await new ClipExportBuilder(videos, storage, NullLogger<ClipExportBuilder>.Instance)
            .BuildAsync(Export, [a, b, c], maxPartBytes: 650_000, () => { heartbeats++; return Task.CompletedTask; },
                CancellationToken.None);

        Assert.Equal([1, 2], parts.Select(part => part.Index));
        Assert.Equal([2, 1], parts.Select(part => part.ClipCount));
        Assert.Equal(3, heartbeats);

        using ZipArchive first = new(new MemoryStream(storage.Objects[ExportParts.Key(Export.UserId, Export.Id, 1)]));
        Assert.Equal(["2026-10-23 Apex Legends - one.mp4", "2026-10-23 Apex Legends - two.mp4"],
            first.Entries.Select(entry => entry.FullName));
        using MemoryStream copy = new();
        await using (Stream entry = first.Entries[1].Open())
        {
            await entry.CopyToAsync(copy);
        }

        Assert.Equal(videos.Bytes[b.Id], copy.ToArray());
        Assert.Equal(parts[0].SizeBytes, storage.Objects[ExportParts.Key(Export.UserId, Export.Id, 1)].Length);
    }

    [Fact]
    public async Task ClipsWithNoVideo_AreLeftOut_AndAnEmptyPartIsNotKept()
    {
        FakeVideos videos = new();
        FakeStorage storage = new();
        ExportClipRow gone = Clip("gone", 600_000), kept = Clip("kept", 600_000);
        videos.Bytes[kept.Id] = new byte[1_000];

        List<ClipExportPart> parts = await new ClipExportBuilder(videos, storage, NullLogger<ClipExportBuilder>.Instance)
            .BuildAsync(Export, [gone, kept], maxPartBytes: 650_000, () => Task.CompletedTask, CancellationToken.None);

        ClipExportPart part = Assert.Single(parts);
        Assert.Equal((1, 1), (part.Index, part.ClipCount));
        Assert.Equal([ExportParts.Key(Export.UserId, Export.Id, 1)], storage.Objects.Keys);
    }

    [Fact]
    public async Task AnUploadThatFails_FailsTheBuild_InsteadOfHangingTheZip()
    {
        FakeVideos videos = new();
        FakeStorage storage = new() { FailAfterBytes = 100_000 };
        ExportClipRow clip = Clip("big", 5_000_000);
        videos.Bytes[clip.Id] = new byte[5_000_000];

        Task build = new ClipExportBuilder(videos, storage, NullLogger<ClipExportBuilder>.Instance)
            .BuildAsync(Export, [clip], maxPartBytes: long.MaxValue, () => Task.CompletedTask, CancellationToken.None);

        Task finished = await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(build, finished);
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => build);
        Assert.Contains("storage is down", error.ToString());
    }

    internal sealed class FakeVideos : IExportVideoSource
    {
        public Dictionary<Guid, byte[]> Bytes { get; } = [];

        public Task<ExportVideo?> OpenAsync(ExportClipRow clip, CancellationToken cancellationToken) =>
            Task.FromResult(Bytes.TryGetValue(clip.Id, out byte[]? bytes)
                ? new ExportVideo(new MemoryStream(bytes))
                : null);
    }

    internal sealed class FakeStorage : IExportStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public long? FailAfterBytes { get; set; }
        public bool IsConfigured => true;

        public async Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            using MemoryStream copy = new();
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                copy.Write(buffer, 0, read);
                if (copy.Length > FailAfterBytes)
                {
                    throw new IOException("storage is down");
                }
            }

            Objects[key] = copy.ToArray();
        }

        public Task DeletePrefixAsync(string prefix)
        {
            foreach (string key in Objects.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Objects.Remove(key);
            }

            return Task.CompletedTask;
        }

        public string DownloadUrl(string key, string fileName) => $"https://r2.test/{key}";
    }
}
