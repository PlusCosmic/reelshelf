using System.Globalization;
using FFMpegCore;
using FFMpegCore.Pipes;
using Reelshelf.Games;
using Xunit;

namespace Reelshelf.Test.Games;

public class ClothColorTests
{
    [Fact]
    public void FromRgb_TakesTheCoverHue_AndMutesIt()
    {
        string cloth = ClothColor.FromRgb(Pixels((230, 30, 30), 100))!;

        (int r, int g, int b) = Parse(cloth);
        Assert.True(r > g && r > b, $"{cloth} should stay red");
        // Pure red is far more saturated than any bookcloth; the result is pulled well back from it.
        Assert.True(g > 40 && b > 40, $"{cloth} should be muted");
    }

    [Fact]
    public void FromRgb_LetsASmallAccentWin_OverAGreyCover()
    {
        // Mostly grey with a green accent, like a cover whose colour is all in the logo.
        byte[] pixels = [.. Pixels((120, 120, 120), 85), .. Pixels((60, 200, 60), 15)];

        (int r, int g, int b) = Parse(ClothColor.FromRgb(pixels)!);

        Assert.True(g > r && g > b);
    }

    [Fact]
    public void FromRgb_GivesAGreyCoverAGreyCloth()
    {
        (int r, int g, int b) = Parse(ClothColor.FromRgb(Pixels((150, 150, 150), 100))!);

        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void FromRgb_KeepsLightAndDarkCoversInTheClothBand()
    {
        (int lr, int lg, int lb) = Parse(ClothColor.FromRgb(Pixels((200, 240, 255), 100))!);
        (int dr, int dg, int db) = Parse(ClothColor.FromRgb(Pixels((10, 20, 60), 100))!);

        Assert.True(Math.Max(lr, Math.Max(lg, lb)) < 200, "a pale cover still gets a mid-tone cloth");
        Assert.True(Math.Max(dr, Math.Max(dg, db)) > 40, "a near-black cover still gets a visible cloth");
    }

    [Fact]
    public void FromRgb_ReturnsNull_WhenThereIsNothingButBlackAndWhite()
    {
        byte[] pixels = [.. Pixels((0, 0, 0), 50), .. Pixels((255, 255, 255), 50)];

        Assert.Null(ClothColor.FromRgb(pixels));
    }

    /// <summary>Runs the real ffmpeg, which CI installs, on a generated solid-colour JPEG.</summary>
    [Fact]
    public async Task FromImageAsync_ReadsACoverImage()
    {
        using MemoryStream image = new();
        await FFMpegArguments
            .FromFileInput("color=c=0x2060c0:size=264x352", verifyExists: false, options => options.ForceFormat("lavfi"))
            .OutputToPipe(new StreamPipeSink(image), options => options
                .WithCustomArgument("-frames:v 1")
                .WithVideoCodec("mjpeg")
                .ForceFormat("image2pipe"))
            .ProcessAsynchronously();

        (int r, int g, int b) = Parse((await ClothColor.FromImageAsync(image.ToArray(), CancellationToken.None))!);

        Assert.True(b > r && b > g);
    }

    [Theory]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co9rk1.jpg", true)]
    [InlineData("http://images.igdb.com/igdb/image/upload/t_cover_big/co9rk1.jpg", false)]
    [InlineData("https://example.com/cover.jpg", false)]
    [InlineData("https://images.igdb.com.example.com/cover.jpg", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData(null, false)]
    public void CanFetch_OnlyAllowsIgdbCoversOverHttps(string? url, bool expected)
    {
        Assert.Equal(expected, GameCoverColors.CanFetch(url));
    }

    private static byte[] Pixels((byte R, byte G, byte B) colour, int count)
    {
        byte[] pixels = new byte[count * 3];
        for (int i = 0; i < count; i++)
        {
            pixels[i * 3] = colour.R;
            pixels[i * 3 + 1] = colour.G;
            pixels[i * 3 + 2] = colour.B;
        }

        return pixels;
    }

    private static (int R, int G, int B) Parse(string hex)
    {
        Assert.Matches("^#[0-9a-f]{6}$", hex);
        return (
            int.Parse(hex[1..3], NumberStyles.HexNumber),
            int.Parse(hex[3..5], NumberStyles.HexNumber),
            int.Parse(hex[5..7], NumberStyles.HexNumber));
    }
}
