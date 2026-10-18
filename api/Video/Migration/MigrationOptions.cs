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
}
