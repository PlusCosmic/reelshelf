using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class ExportPartsTests
{
    private static ExportClipRow Clip(long? size, string? title = "Triple", string? game = "Apex Legends") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        GameName = game,
        CreatedAt = DateTimeOffset.Parse("2026-10-23T18:00:00Z"),
        StorageSize = size
    };

    [Fact]
    public void Parts_FillUpToTheLimit_InOrder()
    {
        ExportClipRow a = Clip(40), b = Clip(40), c = Clip(40), d = Clip(10);

        List<List<ExportClipRow>> parts = ExportParts.Plan([a, b, c, d], maxPartBytes: 100);

        Assert.Equal([[a, b], [c, d]], parts);
    }

    [Fact]
    public void AClipLargerThanAPart_GetsAPartOfItsOwn()
    {
        ExportClipRow small = Clip(10), huge = Clip(500), after = Clip(10);

        Assert.Equal([[small], [huge], [after]], ExportParts.Plan([small, huge, after], maxPartBytes: 100));
    }

    [Fact]
    public void AClipOfUnknownSize_CountsAsAFullLengthClip()
    {
        List<List<ExportClipRow>> parts =
            ExportParts.Plan([Clip(null), Clip(null)], maxPartBytes: ExportParts.UnknownClipBytes + 1);

        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Names_CarryTheDateGameAndTitle_AndAreUnique()
    {
        HashSet<string> taken = [];

        Assert.Equal("2026-10-23 Apex Legends - Triple.mp4", ExportParts.EntryName(Clip(1), taken));
        Assert.Equal("2026-10-23 Apex Legends - Triple (2).mp4", ExportParts.EntryName(Clip(1), taken));
        Assert.Equal("2026-10-23 Apex Legends - TRIPLE (3).mp4", ExportParts.EntryName(Clip(1, "TRIPLE"), taken));
        Assert.Equal("2026-10-23 Clip.mp4", ExportParts.EntryName(Clip(1, null, null), taken));
        Assert.Equal("2026-10-23 a b.mp4", ExportParts.EntryName(Clip(1, "a/b.mp4", ""), taken));
    }

    [Fact]
    public void Keys_AreUnderTheAccountsExportsPrefix()
    {
        Guid user = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid export = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.Equal($"exports/{user}/{export}/reelshelf-clips-3.zip", ExportParts.Key(user, export, 3));
    }
}
