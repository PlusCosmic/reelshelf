namespace Reelshelf.Moderation;

/// <summary>Why a shared clip was reported, as <c>content_report.reason</c> stores it.</summary>
public static class ContentReportReasons
{
    public const string ChildSafety = "child_safety";
    public const string Terrorism = "terrorism";
    public const string Sexual = "sexual";
    public const string Violence = "violence";
    public const string HateOrHarassment = "hate_or_harassment";
    public const string Copyright = "copyright";
    public const string Other = "other";

    private static readonly Dictionary<string, string> Labels = new()
    {
        [ChildSafety] = "child sexual abuse or exploitation",
        [Terrorism] = "terrorist or violent extremist content",
        [Sexual] = "sexual content",
        [Violence] = "graphic violence",
        [HateOrHarassment] = "hate or harassment",
        [Copyright] = "copyright infringement",
        [Other] = "breaking the terms of service"
    };

    /// <summary>
    /// Reasons serious enough that the share link stops working as soon as one is reported, until an admin reviews
    /// it. Share links are unlisted, so a false report costs the owner little; a true one is off the site at once.
    /// </summary>
    public static readonly IReadOnlyList<string> Urgent = [ChildSafety, Terrorism];

    /// <summary>The <see cref="Urgent"/> reasons as a SQL list, for <c>reason IN (...)</c>.</summary>
    public static readonly string UrgentSqlList = string.Join(", ", Urgent.Select(reason => $"'{reason}'"));

    public static bool IsKnown(string? reason) => reason is not null && Labels.ContainsKey(reason);

    public static bool IsUrgent(string reason) => Urgent.Contains(reason);

    /// <summary>What the reason means, in words fit for an email.</summary>
    public static string Describe(string reason) => Labels.GetValueOrDefault(reason, Labels[Other]);
}
