using System.Globalization;
using Amazon;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// AWS spend from Cost Explorer's <c>GetCostAndUsage</c>: unblended cost, daily, by service, from the first of last
/// month (or 30 days back) to today, in as few requests as its pages allow. Cost Explorer charges per request and its
/// endpoint is in us-east-1 whatever the project's Region, so it is read every <see
/// cref="OpsCostsOptions.AwsRefreshInterval"/> with a key of its own (<c>Ops:Costs:Aws</c>) that needs only
/// <c>ce:GetCostAndUsage</c>.
/// </summary>
public sealed class AwsCostSource(IOptionsMonitor<OpsCostsOptions> options, HttpClientFactory? httpClientFactory = null)
    : ICostSource
{
    public const string Name = "aws";
    private const string Metric = "UnblendedCost";

    public string Provider => Name;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.CurrentValue.Aws.AccessKeyId) ||
        !string.IsNullOrWhiteSpace(options.CurrentValue.Aws.SecretAccessKey);

    public TimeSpan RefreshEvery(OpsCostsOptions current) => current.AwsRefreshInterval;

    public async Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        OpsCostsAwsOptions aws = options.CurrentValue.Aws;
        if (string.IsNullOrWhiteSpace(aws.AccessKeyId) || string.IsNullOrWhiteSpace(aws.SecretAccessKey))
        {
            throw new CostSourceException("Ops:Costs:Aws needs both AccessKeyId and SecretAccessKey.");
        }

        AmazonCostExplorerConfig config = new() { RegionEndpoint = RegionEndpoint.USEast1 };
        if (httpClientFactory is not null)
        {
            config.HttpClientFactory = httpClientFactory;
        }

        using AmazonCostExplorerClient client = new(
            new BasicAWSCredentials(aws.AccessKeyId.Trim(), aws.SecretAccessKey.Trim()), config);
        List<CostEntry> entries = [];
        string? currency = null;
        string? token = null;
        do
        {
            GetCostAndUsageResponse response;
            try
            {
                response = await client.GetCostAndUsageAsync(new GetCostAndUsageRequest
                {
                    TimePeriod = new DateInterval
                    {
                        Start = Day(CostWindow.ReadStart(today)),
                        // Exclusive, so today's partial day is in.
                        End = Day(today.AddDays(1))
                    },
                    Granularity = Granularity.DAILY,
                    Metrics = [Metric],
                    GroupBy = [new GroupDefinition { Type = GroupDefinitionType.DIMENSION, Key = "SERVICE" }],
                    NextPageToken = token
                }, cancellationToken);
            }
            catch (AmazonServiceException ex)
            {
                throw new CostSourceException($"Cost Explorer refused: {ex.ErrorCode ?? ex.StatusCode.ToString()}: {ex.Message}", ex);
            }

            foreach (ResultByTime result in response.ResultsByTime ?? [])
            {
                if (!DateOnly.TryParseExact(result.TimePeriod?.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateOnly day))
                {
                    continue;
                }

                foreach (Group group in result.Groups ?? [])
                {
                    if (group.Metrics is null || !group.Metrics.TryGetValue(Metric, out MetricValue? value) ||
                        !decimal.TryParse(value.Amount, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out decimal amount))
                    {
                        continue;
                    }

                    currency ??= value.Unit;
                    entries.Add(new CostEntry(day, group.Keys?.FirstOrDefault() ?? CostWindow.OtherName, amount));
                }
            }

            token = string.IsNullOrEmpty(response.NextPageToken) ? null : response.NextPageToken;
        } while (token is not null);

        return CostWindow.Summarize(currency ?? "USD", entries, today);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
