namespace Reelshelf.ClipBin;

/// <summary>The bin (V55), under <c>Bin</c>.</summary>
public sealed class ClipBinOptions
{
    public const string SectionName = "Bin";

    /// <summary>How long a clip stays in the bin before the sweep deletes it for good. The privacy policy quotes it.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Runs the sweep that empties clips out of the bin once <see cref="Retention"/> is up.</summary>
    public bool SweepEnabled { get; set; } = true;

    /// <summary>How often the sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);
}
