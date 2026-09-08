using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Reelshelf;

/// <summary>
/// Named rate-limit policies for endpoints that spend a shared, finite budget (upstream API quota, Bunny
/// operations). Partitions are per signed-in user (falling back to the client address for anonymous calls)
/// so one account cannot exhaust the budget for everyone.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Game search and add-from-IGDB, which call IGDB with the application's shared credentials.</summary>
    public const string Igdb = "igdb";

    /// <summary>Clip preparation, which reserves storage and creates a Bunny video before any bytes arrive.</summary>
    public const string ClipPrepare = "clip-prepare";

    private const int DefaultIgdbRequestsPerMinute = 30;

    // The uploader runs at most three uploads at once, so a legitimate client prepares clips no faster than
    // it finishes them; this bound only bites on scripted bursts of never-uploaded reservations.
    private const int DefaultClipPreparationsPerMinute = 60;

    public static void AddReelshelfRateLimiting(this WebApplicationBuilder builder)
    {
        int igdbPerMinute = builder.Configuration.GetValue<int?>("Igdb:RequestsPerUserPerMinute") ?? DefaultIgdbRequestsPerMinute;
        int preparationsPerMinute = builder.Configuration.GetValue<int?>("Uploads:PreparationsPerUserPerMinute") ?? DefaultClipPreparationsPerMinute;

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
                    detail = "You are doing that too quickly. Wait a moment and try again."
                }, cancellationToken);
            };

            options.AddPerUserSlidingWindow(Igdb, igdbPerMinute);
            options.AddPerUserSlidingWindow(ClipPrepare, preparationsPerMinute);
        });
    }

    private static void AddPerUserSlidingWindow(this RateLimiterOptions options, string policyName, int permitsPerMinute)
    {
        options.AddPolicy(policyName, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
            PartitionKey(httpContext),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permitsPerMinute),
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
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
