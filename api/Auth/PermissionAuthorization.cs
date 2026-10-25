namespace Reelshelf.Auth;

/// <summary>
/// Endpoint filter that checks if the authenticated user has the required permission.
/// Returns 403 Forbidden if the user lacks the permission.
/// </summary>
public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequirePermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(AuthenticatedUser.HttpContextKey, out var item) ||
            item is not AuthenticatedUser user)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.HasPermission(_permission))
        {
            return TypedResults.Forbid();
        }

        return await next(context);
    }
}

/// <summary>
/// Extension methods for applying permission requirements to endpoints.
/// </summary>
public static class PermissionAuthorizationExtensions
{
    /// <summary>
    /// Requires the user to have the specified permission to access this endpoint.
    /// Returns 403 Forbidden if the user lacks the permission.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new RequirePermissionFilter(permission));
    }
}
