using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class OverLimitTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-10-01T00:00:00Z");

    private static OwnedClip Clip(int day, int? pickedDay = null) =>
        new(Guid.NewGuid(), $"clip {day}", Day.AddDays(day), pickedDay is { } picked ? Day.AddDays(picked) : null, "Apex");

    [Fact]
    public void TheNewestClipsAreKept()
    {
        OwnedClip old = Clip(1), middle = Clip(2), newest = Clip(3);

        (List<OwnedClip> kept, List<OwnedClip> over) = OverLimit.Split([old, newest, middle], 2);

        Assert.Equal([newest, middle], kept);
        Assert.Equal([old], over);
    }

    [Fact]
    public void PickedClipsAreKeptFirst_TheLatestPickedFirst()
    {
        OwnedClip oldPickedFirst = Clip(1, pickedDay: 10), oldPickedLater = Clip(2, pickedDay: 11), newest = Clip(9);

        (List<OwnedClip> kept, List<OwnedClip> over) = OverLimit.Split([newest, oldPickedFirst, oldPickedLater], 1);

        Assert.Equal([oldPickedLater], kept);
        Assert.Equal([oldPickedFirst, newest], over);
    }

    [Fact]
    public void WithinTheLimit_OrWithNoLimit_NothingIsOver()
    {
        OwnedClip[] clips = [Clip(1), Clip(2)];

        Assert.Empty(OverLimit.Split(clips, 2).Over);
        Assert.Empty(OverLimit.Split(clips, null).Over);
    }
}
