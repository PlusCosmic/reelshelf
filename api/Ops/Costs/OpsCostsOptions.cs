namespace Reelshelf.Ops.Costs;

/// <summary>
/// <c>Ops:Costs</c>: where the monitor's Costs section reads spend from. Every provider is optional; one without its
/// credentials reports itself not configured and nothing else changes. Keys here are read-only billing keys of their
/// own, never the app's working keys (OpenRouter's working keys are read only for their own usage).
/// </summary>
public sealed class OpsCostsOptions
{
    public const string SectionName = "Ops:Costs";

    /// <summary>How often each provider but AWS is read again.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often AWS Cost Explorer is read again: it charges for every request.</summary>
    public TimeSpan AwsRefreshInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How long after a failed read a provider is tried again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(1);

    public OpsCostsAwsOptions Aws { get; set; } = new();
    public OpsCostsOpenRouterOptions OpenRouter { get; set; } = new();
    public OpsCostsAdminKeyOptions OpenAi { get; set; } = new();
    public OpsCostsAdminKeyOptions Anthropic { get; set; } = new();
}

/// <summary>An IAM user's access key allowed only <c>ce:GetCostAndUsage</c>.</summary>
public sealed class OpsCostsAwsOptions
{
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
}

public sealed class OpsCostsOpenRouterOptions
{
    /// <summary>
    /// A management key: the account's daily spend by model for the last 30 completed days (<c>/api/v1/activity</c>)
    /// and its credit left (<c>/api/v1/credits</c>).
    /// </summary>
    public string ManagementKey { get; set; } = "";

    /// <summary>
    /// An API key whose month-to-date and today's usage count, besides the app's own OpenRouter keys
    /// (<c>ClipTranscription:OpenRouterApiKey</c>, <c>LegendDetection</c>'s, <c>ClipSummary</c>'s and
    /// <c>ClipSearch</c>'s <c>Providers:openrouter:ApiKey</c>).
    /// </summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>An organization admin key (OpenAI's <c>sk-admin-…</c>, Anthropic's <c>sk-ant-admin01-…</c>).</summary>
public sealed class OpsCostsAdminKeyOptions
{
    public string AdminKey { get; set; } = "";
}
