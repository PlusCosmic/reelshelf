using Reelshelf.Devices;
using Xunit;

namespace Reelshelf.Test.Devices;

public class DeviceCodesTests
{
    [Fact]
    public void UserCodes_AreEightLettersFromTheAlphabet_ShownWithADash()
    {
        for (int i = 0; i < 200; i++)
        {
            string code = DeviceCodes.NewUserCode();
            Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{8}$", code);
            Assert.Equal($"{code[..4]}-{code[4..]}", DeviceCodes.FormatUserCode(code));
        }
    }

    [Theory]
    [InlineData("KDRW-TPLM", "KDRWTPLM")]
    [InlineData("kdrwtplm", "KDRWTPLM")]
    [InlineData(" kdrw tplm ", "KDRWTPLM")]
    [InlineData("KDRW-TPL", null)]
    [InlineData("KDRW-TPLMB", null)]
    [InlineData("KDRW-TPLA", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TypedCodes_AreMatchedIgnoringCaseAndTheDash(string? typed, string? expected)
    {
        Assert.Equal(expected, DeviceCodes.NormalizeUserCode(typed));
    }

    [Fact]
    public void Tokens_CarryThePrefix_AndAreNeverRepeated()
    {
        HashSet<string> tokens = Enumerable.Range(0, 100).Select(_ => DeviceCodes.NewToken()).ToHashSet();

        Assert.Equal(100, tokens.Count);
        Assert.All(tokens, token =>
        {
            Assert.StartsWith("rsd_", token);
            Assert.Matches("^rsd_[A-Za-z0-9_-]{43}$", token);
            Assert.True(DeviceCodes.LooksLikeToken(token));
        });
        Assert.False(DeviceCodes.LooksLikeToken("a-worker-secret-of-at-least-32-characters"));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", DeviceCodes.NewDeviceCode());
    }

    [Theory]
    [InlineData("uploader/1.1.9", "1.2.0", true)]
    [InlineData("uploader/1.2", "1.2.0", false)]
    [InlineData("uploader/1.2.0", "1.2.0", false)]
    [InlineData("uploader/1.10.0", "1.2.0", false)]
    [InlineData("uploader/1.2.0-beta", "1.2.0", false)]
    [InlineData("uploader/nonsense", "1.2.0", true)]
    [InlineData("uploader/nonsense", "0.0", false)]
    [InlineData("Uploader/0.9.0", "1.0", true)]
    [InlineData(null, "9.0", false)]
    [InlineData("", "9.0", false)]
    [InlineData("website/0.1", "9.0", false)]
    public void TheVersionGate_ComparesUploaderVersions(string? header, string minimum, bool tooOld)
    {
        Assert.Equal(tooOld, ClientVersionGate.IsTooOld(header, Version.Parse(minimum)));
    }
}
