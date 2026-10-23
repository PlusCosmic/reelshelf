namespace Reelshelf.Plans;

/// <summary>Going over the limit and exports (ADR-0007), under <c>Plans</c>.</summary>
public sealed class PlanOptions
{
    public const string SectionName = "Plans";

    /// <summary>
    /// Runs the over-limit sweep: starts and ends graces, sends their mail, and exports then deletes the clips over
    /// the limit once a grace ends. Off by default, since it deletes clips; exports the owner asks for work either way.
    /// </summary>
    public bool Enforce { get; set; }

    /// <summary>How long clips over the limit are kept after the account goes over it.</summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromDays(14);

    /// <summary>How long before the grace ends the "a few days left" mail goes out.</summary>
    public TimeSpan GraceWarningBefore { get; set; } = TimeSpan.FromDays(3);

    /// <summary>How often the sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long an export's zips can be downloaded. The bucket's rule on <c>exports/</c> deletes them a day later.</summary>
    public TimeSpan ExportRetention { get; set; } = TimeSpan.FromDays(28);

    public long ExportMaxPartBytes { get; set; } = ExportParts.DefaultMaxPartBytes;

    public int ExportMaxAttempts { get; set; } = 3;

    public TimeSpan ExportRetryAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>An export still running with no heartbeat for this long lost its process; it is claimed again.</summary>
    public TimeSpan ExportStaleAfter { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often the export service looks for work.</summary>
    public TimeSpan ExportPollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
