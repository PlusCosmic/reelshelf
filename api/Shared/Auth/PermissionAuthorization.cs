using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Nucleus.Shared.Auth;

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
/// Endpoint filter that checks if the authenticated user has any of the required permissions.
/// Returns 403 Forbidden if the user lacks all specified permissions.
/// </summary>
public class RequireAnyPermissionFilter : IEndpointFilter
{
    private readonly string[] _permissions;

    public RequireAnyPermissionFilter(params string[] permissions)
    {
        _permissions = permissions;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(AuthenticatedUser.HttpContextKey, out var item) ||
            item is not AuthenticatedUser user)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.HasAnyPermission(_permissions))
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

    /// <summary>
    /// Requires the user to have any of the specified permissions to access this endpoint.
    /// Returns 403 Forbidden if the user lacks all specified permissions.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new RequireAnyPermissionFilter(permissions));
    }

    /// <summary>
    /// Checks if the authenticated user has a permission and returns a typed Forbid result if not.
    /// Use this for inline permission checks within endpoint handlers.
    /// </summary>
    /// <example>
    /// if (user.DenyIfMissing(Permissions.ClipsDelete, out var forbidden))
    ///     return forbidden;
    /// </example>
    public static bool DenyIfMissing(
        this AuthenticatedUser user,
        string permission,
        out ForbidHttpResult forbidden)
    {
        if (!user.HasPermission(permission))
        {
            forbidden = TypedResults.Forbid();
            return true;
        }

        forbidden = default!;
        return false;
    }

    /// <summary>
    /// Checks if the authenticated user has any of the specified permissions and returns a typed Forbid result if not.
    /// </summary>
    public static bool DenyIfMissingAny(
        this AuthenticatedUser user,
        string[] permissions,
        out ForbidHttpResult forbidden)
    {
        if (!user.HasAnyPermission(permissions))
        {
            forbidden = TypedResults.Forbid();
            return true;
        }

        forbidden = default!;
        return false;
    }
}
