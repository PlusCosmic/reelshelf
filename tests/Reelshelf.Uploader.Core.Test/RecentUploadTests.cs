using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.State;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>S4's rows, in the words screens.html gives each state.</summary>
public class RecentUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 21, 30, 0, TimeSpan.Zero);
    private static readonly Book Apex = new(Guid.NewGuid(), "Apex Legends", "apex-legends", "#8a3b2f");

    [Theory]
    [InlineData(LedgerState.Waiting, null, 0, "Waiting")]
    [InlineData(LedgerState.Uploading, 64, 0, "Uploading… 64%")]
    [InlineData(LedgerState.Shelved, null, 0, "On the shelf")]
    [InlineData(LedgerState.Duplicate, null, 0, "Already on the shelf")]
    [InlineData(LedgerState.TooLong, null, 0, "Too long for your plan")]
    [InlineData(LedgerState.Uploading, null, 2, "Didn't upload, trying again soon")]
    [InlineData(LedgerState.Failed, null, 5, "Didn't upload")]
    [InlineData(LedgerState.NeedsBook, null, 0, "Waiting for a book")]
    public void EachState_ReadsAsTheScreensSay(LedgerState state, int? percent, int attempts, string status)
    {
        LedgerEntry entry = Entry(state, attempts);

        RecentUpload row = RecentUpload.From(entry, [Apex], Now, percent, TimeZoneInfo.Utc);

        Assert.Equal(status, row.Status);
        Assert.Equal("Apex Legends 2026.11.06 - 21.20.08.01", row.Title);
        Assert.Equal(state == LedgerState.Failed, row.CanTryAgain);
    }

    [Fact]
    public void AShelvedClip_ShowsItsBookLengthSizeAndTime_AndOpensOnTheSite()
    {
        LedgerEntry entry = Entry(LedgerState.Shelved, 0);

        RecentUpload row = RecentUpload.From(entry, [Apex], Now, zone: TimeZoneInfo.Utc);

        Assert.Equal("Apex Legends · 0:22 · 40 MB · 21:23", row.Meta);
        Assert.True(row.CanOpen);
        Assert.Equal("apex-legends", row.BookSlug);
        Assert.Equal("#8a3b2f", row.ClothColor);
    }

    private static LedgerEntry Entry(LedgerState state, int attempts) => new()
    {
        Root = "/videos",
        Path = "Apex Legends/Apex Legends 2026.11.06 - 21.20.08.01.mp4",
        Size = 41_943_040,
        LastWriteUtc = Now.AddMinutes(-10),
        State = state,
        CategoryId = Apex.Id,
        ClipId = state == LedgerState.Shelved ? Guid.NewGuid() : null,
        LengthSeconds = 22,
        Attempts = attempts,
        NextAttemptAt = attempts is > 0 and < 5 ? Now.AddMinutes(1) : null,
        CreatedAt = Now.AddMinutes(-10),
        UpdatedAt = Now,
        ShelvedAt = state == LedgerState.Shelved ? new DateTimeOffset(2026, 11, 6, 21, 23, 0, TimeSpan.Zero) : null
    };
}
