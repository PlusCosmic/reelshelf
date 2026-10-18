using Xunit;

namespace Reelshelf.Worker.Test;

public class FfmpegRunnerTests
{
    [Theory]
    [InlineData(1, "exited with code 1")]
    [InlineData(139, "crashed with SIGSEGV (exit code 139)")]
    [InlineData(137, "was killed with SIGKILL (exit code 137)")]
    [InlineData(132, "crashed with SIGILL (exit code 132)")]
    [InlineData(255, "exited with code 255")]
    public void DescribeExit_NamesTheSignalAProcessDiedOf(int exitCode, string expected)
    {
        Assert.StartsWith(expected, FfmpegRunner.DescribeExit(exitCode));
    }
}
