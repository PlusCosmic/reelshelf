namespace Reelshelf.Video.Migration;

/// <summary>
/// Moving the clips still on Bunny onto our own pipeline (docs/video-pipeline/bunny-migration.md). Read on every
/// pass, so turning it on or off needs no restart.
/// </summary>
public sealed class MigrationOptions
{
    public const string SectionName = "Migration";

    /// <summary>
    /// Queue <c>migrate</c> jobs for ready Bunny clips (<see cref="BunnyMigrationBackfillService"/>). Off by default;
    /// turn it on per instance, dev first.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many <c>migrate</c> jobs the backfill keeps pending or running at once. Small, so the queue never holds
    /// the whole library and turning the migration off leaves little queued.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    public int EffectiveBatchSize => Math.Max(0, BatchSize);

    /// <summary>
    /// Delete the Bunny copies of clips that moved more than <see cref="DeleteBunnyAfter"/> ago, and the Bunny
    /// collections nothing refers to any more (<see cref="BunnyCopySweepService"/>). Off by default; turn it on per
    /// instance once its clips have moved, dev first.
    /// </summary>
    public bool DeleteBunny { get; set; }

    /// <summary>
    /// How long a migrated clip's Bunny copy is kept, counted from <c>migrated_at</c>, so a bad encode can be switched
    /// back. The privacy policy promises at most 7 days.
    /// </summary>
    public TimeSpan DeleteBunnyAfter { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan EffectiveDeleteBunnyAfter => DeleteBunnyAfter < TimeSpan.Zero ? TimeSpan.Zero : DeleteBunnyAfter;
}
