using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Reelshelf;

/// <summary>
/// Named rate-limit policies for endpoints that spend a shared, finite upstream budget. Partitions are per
/// signed-in user (falling back to the client address for anonymous calls) so one account cannot exhaust
/// the budget for everyone.
/// </summary>
public static class RateLimitPolicies
{
    public const string Igdb = "igdb";

    private const int DefaultIgdbRequestsPerMinute = 30;

    public static void AddReelshelfRateLimiting(this WebApplicationBuilder builder)
    {
        int igdbPerMinute = builder.Configuration.GetValue<int?>("Igdb:RequestsPerUserPerMinute") ?? DefaultIgdbRequestsPerMinute;

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "You are searching too quickly. Wait a moment and try again."
                }, cancellationToken);
            };

            options.AddPolicy(Igdb, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
                PartitionKey(httpContext),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, igdbPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));
        });
    }

    private static string PartitionKey(HttpContext httpContext)
    {
        string? userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
