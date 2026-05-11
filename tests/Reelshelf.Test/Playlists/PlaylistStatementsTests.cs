using Reelshelf.Playlists;
using Xunit;

namespace Reelshelf.Test.Playlists;

public class PlaylistStatementsTests
{
    [Fact]
    public void ToDatabaseDate_ConvertsDateOnlyToMidnightDateTime()
    {
        DateTime databaseDate = PlaylistStatements.ToDatabaseDate(new DateOnly(2026, 5, 10));

        Assert.Equal(new DateTime(2026, 5, 10, 0, 0, 0), databaseDate);
        Assert.Equal(DateTimeKind.Unspecified, databaseDate.Kind);
    }
}
