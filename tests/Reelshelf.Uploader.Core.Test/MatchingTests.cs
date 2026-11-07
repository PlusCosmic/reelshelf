using Reelshelf.Uploader.Core.Matching;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>
/// Which book a clip goes on, and when it was made: the frontend's <c>assignGame</c> and <c>deriveCreatedAt</c> cases
/// (<c>frontend/src/hooks/bulkUploadQueue.test.ts</c>), then the uploader's own rules.
/// </summary>
public class MatchingTests
{
    private static readonly Book Apex = new(Guid.NewGuid(), "Apex Legends", "apex-legends");
    private static readonly Book Valorant = new(Guid.NewGuid(), "Valorant", "valorant");
    private static readonly Book[] Books = [Apex, Valorant];

    // Ported from "builds valid queue rows from files and rejects invalid files".
    [Fact]
    public void AssignGame_MatchesAGameInTheFileName()
    {
        Assert.Equal(new BookMatch(Apex.Id, BookSource.SourcePath),
            BookMatcher.AssignGame("apex-legends_2026-05-10_2147.mp4", Books));
    }

    // Ported from "uses source path game assignment before game-page fallback context".
    [Fact]
    public void AssignGame_UsesTheSourcePath_BeforeTheFallback()
    {
        Assert.Equal(new BookMatch(Apex.Id, BookSource.SourcePath),
            BookMatcher.AssignGame("captures/Apex Legends/clip.mp4", Books, Valorant.Id));
        Assert.Equal(new BookMatch(Valorant.Id, BookSource.Fallback),
            BookMatcher.AssignGame("captures/unknown/clip.mp4", Books, Valorant.Id));
    }

    // Ported from "groups rows by game assignment ..." and "supports selection ...", whose rows are filed by path.
    [Theory]
    [InlineData("apex-legends/one.mp4", "Apex Legends")]
    [InlineData("valorant/two.mp4", "Valorant")]
    [InlineData("apex-one.mp4", null)]
    public void AssignGame_FilesRowsByTheirPath(string path, string? book)
    {
        Assert.Equal(Books.FirstOrDefault(candidate => candidate.Name == book)?.Id,
            BookMatcher.AssignGame(path, Books).CategoryId);
    }

    [Fact]
    public void AssignGame_WithNothingToGoOn_HasNoBook()
    {
        Assert.Equal(new BookMatch(null, null), BookMatcher.AssignGame("captures/unknown/clip.mp4", Books));
    }

    [Theory]
    [InlineData("Apex Legends", "Apex Legends")]
    [InlineData("VALORANT", "Valorant")]
    [InlineData("apex_legends", "Apex Legends")]
    [InlineData("ApexLegends", "Apex Legends")]
    [InlineData("Apex Legends (ranked)", "Apex Legends")]
    [InlineData("Marvel Rivals", null)]
    public void AFolder_MatchesABook_IgnoringCaseAndPunctuation(string folder, string? book)
    {
        Assert.Equal(Books.FirstOrDefault(candidate => candidate.Name == book)?.Id,
            BookMatcher.Resolve(folder, Books, savedBook: null, defaultBook: null).CategoryId);
    }

    [Fact]
    public void TheLongestMatchingName_Wins()
    {
        Book apexMobile = new(Guid.NewGuid(), "Apex Legends Mobile", "apex-legends-mobile");

        Assert.Equal(apexMobile.Id,
            BookMatcher.Resolve("Apex Legends Mobile", [Apex, apexMobile], null, null).CategoryId);
    }

    [Fact]
    public void ASavedChoice_WinsOverTheName_AndTheDefault_CatchesTheRest()
    {
        Assert.Equal(new BookMatch(Valorant.Id, BookSource.Saved),
            BookMatcher.Resolve("Apex Legends", Books, savedBook: Valorant.Id, defaultBook: null));
        Assert.Equal(new BookMatch(Apex.Id, BookSource.Fallback),
            BookMatcher.Resolve("Marvel Rivals", Books, savedBook: null, defaultBook: Apex.Id));
        Assert.Equal(new BookMatch(null, null),
            BookMatcher.Resolve("Marvel Rivals", Books, savedBook: null, defaultBook: null));
    }

    [Fact]
    public void AFileInTheRecordingsFolderItself_GoesToTheDefaultBook_EvenIfItsNameMatches()
    {
        Assert.Equal(new BookMatch(Valorant.Id, BookSource.Fallback),
            BookMatcher.Resolve("", Books, savedBook: null, defaultBook: Valorant.Id));
        Assert.Equal(new BookMatch(null, null), BookMatcher.Resolve("", Books, null, null));
    }

    [Fact]
    public void ABookNoLongerOnTheShelf_CountsAsNone()
    {
        Guid gone = Guid.NewGuid();

        Assert.Equal(Apex.Id, BookMatcher.Resolve("Apex Legends", Books, savedBook: gone, defaultBook: null).CategoryId);
        Assert.Null(BookMatcher.Resolve("Marvel Rivals", Books, savedBook: null, defaultBook: gone).CategoryId);
    }

    [Fact]
    public void NormalizeForMatch_IsTheFrontendsRule()
    {
        Assert.Equal(" apex legends 2026 ", BookMatcher.NormalizeForMatch("  Apex_Legends -- 2026!"));
        Assert.Equal("apexlegends2026", BookMatcher.CompactForMatch("Apex_Legends -- 2026!"));
    }

    // Ported from "derives timestamps from metadata first and filename fallback second".
    [Fact]
    public void DeriveCreatedAt_UsesTheLastWriteTimeFirst_AndTheFileNameSecond()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test");
        DateTimeOffset metadata = new(2026, 5, 11, 6, 0, 0, TimeSpan.FromHours(1));

        DateTimeOffset fromMetadata = ClipDates.DeriveCreatedAt(metadata.ToUniversalTime(), "20260510_214700.mp4", zone);
        DateTimeOffset fromName = ClipDates.DeriveCreatedAt(DateTimeOffset.UnixEpoch, "session_2026-05-10_2147.mp4", zone);

        Assert.Equal(6, TimeZoneInfo.ConvertTime(fromMetadata, zone).Hour);
        Assert.Equal(new DateTimeOffset(2026, 5, 10, 21, 47, 0, TimeSpan.FromHours(1)), fromName);
    }

    [Theory]
    [InlineData("apex-legends_2026-05-10_2147.mp4", 2026, 5, 10, 21, 47, 0)]
    [InlineData("clip 2026-05-10 21-47.mp4", 2026, 5, 10, 21, 47, 0)]
    [InlineData("20260510_214755.mp4", 2026, 5, 10, 21, 47, 55)]
    [InlineData("20260510214755.mp4", 2026, 5, 10, 21, 47, 55)]
    public void TheFileNamePatterns_AreReadAsLocalTime(string name, int year, int month, int day, int hour,
        int minute, int second)
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");

        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(-5)),
            ClipDates.ParseTimestampFromSourcePath(name, zone));
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("2026-02-31_2147.mp4")]
    [InlineData("2026-05-10_2547.mp4")]
    public void ANameWithNoRealTimestamp_FallsBackToTheEpoch(string name)
    {
        Assert.Equal(DateTimeOffset.UnixEpoch, ClipDates.DeriveCreatedAt(DateTimeOffset.UnixEpoch, name));
    }
}
