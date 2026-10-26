using Reelshelf.Auth;

namespace Reelshelf.FeatureFlags;

/// <summary>
/// Answers 404 from an endpoint whose flag is off for the caller, as if the endpoint weren't there. Hiding a feature in
/// the frontend isn't enough: its endpoints carry this too. Put it after <c>RequireAuthorization()</c>, so the caller is
/// known.
/// </summary>
public sealed class RequireFeatureFilter(string key) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(AuthenticatedUser.HttpContextKey, out object? item) ||
            item is not AuthenticatedUser user)
        {
            return TypedResults.Unauthorized();
        }

        FeatureFlagService flags = context.HttpContext.RequestServices.GetRequiredService<FeatureFlagService>();
        if (!await flags.IsEnabledAsync(key, user, context.HttpContext.RequestAborted))
        {
            return TypedResults.NotFound();
        }

        return await next(context);
    }
}

public static class RequireFeatureExtensions
{
    /// <summary>Answers 404 unless the flag is on for the caller.</summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string key)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!FeatureFlagKeys.IsKnown(key))
        {
            throw new ArgumentException($"'{key}' isn't in FeatureFlagKeys.All.", nameof(key));
        }

        return builder.AddEndpointFilter(new RequireFeatureFilter(key));
    }
}
