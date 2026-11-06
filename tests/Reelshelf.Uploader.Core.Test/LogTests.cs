using Reelshelf.Uploader.Core.Logging;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

public class LogTests
{
    [Fact]
    public void TheLog_WritesADailyFile_KeepsFourteenDays_AndNeverATToken()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"reelshelf-logs-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);
        DailyFileLog log = new(directory, () => now);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "uploader-20261106.log"), "old");
            File.WriteAllText(Path.Combine(directory, "uploader-20261107.log"), "kept");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "not ours");

            log.Write("Signed in with rsd_AbC-123_xyz");

            string today = File.ReadAllText(Path.Combine(directory, "uploader-20261120.log"));
            Assert.Contains("Signed in with rsd_[redacted]", today);
            Assert.DoesNotContain("AbC-123", today);
            Assert.False(File.Exists(Path.Combine(directory, "uploader-20261106.log")));
            Assert.True(File.Exists(Path.Combine(directory, "uploader-20261107.log")));
            Assert.True(File.Exists(Path.Combine(directory, "notes.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
