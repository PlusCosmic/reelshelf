using System.Globalization;
using FFMpegCore;
using FFMpegCore.Pipes;

namespace Reelshelf.Games;

/// <summary>
/// The colour a game's book is bound in on the library shelf, taken from its cover: the cover's dominant hue,
/// muted and held to a mid lightness so it reads as bookcloth next to the other spines.
/// </summary>
public static class ClothColor
{
    /// <summary>Covers are shrunk to this many pixels before sampling; enough to find the dominant hue.</summary>
    public const int SampleWidth = 24;
    public const int SampleHeight = 32;

    // Cloth stays in a band a spine title can sit on in either light or dark ink.
    private const double MinLightness = 0.30;
    private const double MaxLightness = 0.60;
    private const double MaxChroma = 0.12;

    // Pixels below this chroma count as grey; covers that are mostly grey get a grey cloth.
    private const double NeutralChroma = 0.035;
    private const double MinColourfulness = 0.008;
    private const int HueBins = 12;

    /// <summary>The cloth colour for a cover image (any format ffmpeg reads). Throws when ffmpeg cannot read it.</summary>
    public static async Task<string?> FromImageAsync(byte[] image, CancellationToken cancellationToken)
    {
        using MemoryStream input = new(image);
        using MemoryStream output = new();
        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input), options => options.ForceFormat("image2pipe"))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithCustomArgument($"-vf scale={SampleWidth}:{SampleHeight}:flags=area")
                .WithCustomArgument("-frames:v 1 -pix_fmt rgb24")
                .ForceFormat("rawvideo"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return FromRgb(output.ToArray());
    }

    /// <summary>
    /// The cloth colour for packed RGB24 pixels, as <c>#rrggbb</c>, or null when every pixel is near black or
    /// near white and there is nothing to take a colour from.
    /// </summary>
    public static string? FromRgb(ReadOnlySpan<byte> rgb)
    {
        var bins = new HueBin[HueBins];
        double neutralLightness = 0;
        int neutralCount = 0;
        int counted = 0;
        double totalChroma = 0;

        for (int i = 0; i + 2 < rgb.Length; i += 3)
        {
            (double l, double a, double b) = ToOklab(rgb[i], rgb[i + 1], rgb[i + 2]);
            // Letterboxing and blown-out skies say nothing about the game.
            if (l < 0.08 || l > 0.97) continue;

            counted++;
            double chroma = Math.Sqrt(a * a + b * b);
            if (chroma < NeutralChroma)
            {
                neutralLightness += l;
                neutralCount++;
                continue;
            }

            double hue = Math.Atan2(b, a) * 180 / Math.PI;
            if (hue < 0) hue += 360;
            int bin = Math.Min(HueBins - 1, (int)(hue / (360.0 / HueBins)));
            bins[bin].Add(chroma, l, a, b);
            totalChroma += chroma;
        }

        if (counted == 0) return null;

        if (totalChroma / counted < MinColourfulness)
        {
            double grey = neutralCount > 0 ? neutralLightness / neutralCount : 0.5;
            return ToHex(Math.Clamp(grey, MinLightness, MaxLightness), 0, 0);
        }

        // The hue with the most colour in it wins; averaging within one bin keeps that hue intact.
        HueBin best = bins.MaxBy(bin => bin.Weight);
        double lightness = Math.Clamp(best.L / best.Weight, MinLightness, MaxLightness);
        double avgA = best.A / best.Weight;
        double avgB = best.B / best.Weight;
        double avgChroma = Math.Sqrt(avgA * avgA + avgB * avgB);
        double scale = avgChroma > MaxChroma ? MaxChroma / avgChroma : 1;
        return ToHex(lightness, avgA * scale, avgB * scale);
    }

    private struct HueBin
    {
        public double Weight;
        public double L;
        public double A;
        public double B;

        public void Add(double weight, double l, double a, double b)
        {
            Weight += weight;
            L += l * weight;
            A += a * weight;
            B += b * weight;
        }
    }

    private static (double L, double A, double B) ToOklab(byte red, byte green, byte blue)
    {
        double r = ToLinear(red / 255.0), g = ToLinear(green / 255.0), b = ToLinear(blue / 255.0);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>Converts to sRGB hex, pulling chroma in until the colour fits the sRGB gamut.</summary>
    private static string ToHex(double lightness, double a, double b)
    {
        for (double factor = 1; factor >= 0; factor -= 0.02)
        {
            (double r, double g, double bl) = FromOklab(lightness, a * factor, b * factor);
            if (r is >= -0.0001 and <= 1.0001 && g is >= -0.0001 and <= 1.0001 && bl is >= -0.0001 and <= 1.0001)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"#{ToByte(r):x2}{ToByte(g):x2}{ToByte(bl):x2}");
            }
        }

        (double gr, double gg, double gb) = FromOklab(lightness, 0, 0);
        return string.Create(CultureInfo.InvariantCulture, $"#{ToByte(gr):x2}{ToByte(gg):x2}{ToByte(gb):x2}");
    }

    private static (double R, double G, double B) FromOklab(double lightness, double a, double b)
    {
        double l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        double m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        double s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (
            ToGamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            ToGamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            ToGamma(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    private static double ToLinear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double ToGamma(double channel) =>
        channel <= 0.0031308 ? 12.92 * channel : 1.055 * Math.Pow(channel, 1 / 2.4) - 0.055;

    private static int ToByte(double channel) => (int)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
