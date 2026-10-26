using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Billing;
using Reelshelf.Exceptions;

namespace Reelshelf.Auth;

public sealed record AdminAccountIdentity(string Provider, string ProviderUserId, string Username);

/// <summary>An account's role and granted tier; roles and tiers are lowercase names.</summary>
public sealed record AdminAccount(
    Guid Id,
    string Username,
    string? GlobalName,
    string? AvatarUrl,
    string Role,
    string? GrantedTier,
    List<AdminAccountIdentity> Identities);

public sealed record AdminAccessChange(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    string Reason,
    string? ChangedByName,
    DateTimeOffset ChangedAt);

/// <summary>The account, the tier it has now (granted or paid for), and its recorded changes, newest first.</summary>
public sealed record AdminAccountDetail(AdminAccount Account, string Tier, List<AdminAccessChange> Changes);

public sealed record SetAccountRoleRequest(string Role, string? Reason);

/// <summary>A null <see cref="Tier"/> takes the grant away.</summary>
public sealed record SetGrantedTierRequest(string? Tier, string? Reason);

/// <summary>Admins find accounts and change their role or granted tier (ADR-0008).</summary>
public static class AdminAccountEndpoints
{
    private const int SearchLimit = 20;

    public static void MapAdminAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("admin/accounts")
            .RequireAuthorization()
            .RequirePermission(Permissions.AdminUsers);

        group.MapGet("", FindAccounts).WithName("FindAdminAccounts");
        group.MapGet("{userId:guid}", GetAccount).WithName("GetAdminAccount");
        group.MapPut("{userId:guid}/role", SetRole).WithName("SetAdminAccountRole");
        group.MapPut("{userId:guid}/granted-tier", SetGrantedTier).WithName("SetAdminAccountGrantedTier");
    }

    /// <summary>Accounts matching a username, display name or Discord or Twitch id; nothing under two characters.</summary>
    private static async Task<Ok<List<AdminAccount>>> FindAccounts(IAccountAccessStore store, string? query)
    {
        string trimmed = query?.Trim() ?? "";
        if (trimmed.Length < 2)
        {
            return TypedResults.Ok(new List<AdminAccount>());
        }

        List<AccountAccessRow> rows = await store.FindAccounts(trimmed, SearchLimit);
        return TypedResults.Ok(rows.Select(ToAccount).ToList());
    }

    private static async Task<Results<Ok<AdminAccountDetail>, NotFound>> GetAccount(
        Guid userId,
        IAccountAccessStore store,
        AccountTierService tiers)
    {
        AccountAccessRow? row = await store.GetAccount(userId);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await Detail(row, store, tiers));
    }

    private static async Task<Results<Ok<AdminAccountDetail>, NotFound>> SetRole(
        Guid userId,
        SetAccountRoleRequest request,
        AuthenticatedUser admin,
        AccountAccessService access,
        IAccountAccessStore store,
        AccountTierService tiers)
    {
        UserRole role = UserRoles.TryParse(request.Role)
                        ?? throw new BadRequestException("The role must be viewer, editor or admin.");
        await access.SetRole(admin, userId, role, request.Reason);
        return await GetAccount(userId, store, tiers);
    }

    private static async Task<Results<Ok<AdminAccountDetail>, NotFound>> SetGrantedTier(
        Guid userId,
        SetGrantedTierRequest request,
        AuthenticatedUser admin,
        AccountAccessService access,
        IAccountAccessStore store,
        AccountTierService tiers)
    {
        AccountTier? tier = null;
        if (!string.IsNullOrWhiteSpace(request.Tier))
        {
            tier = GrantedTiers.Parse(request.Tier.Trim())
                   ?? throw new BadRequestException("Only plus, pro or unlimited can be granted.");
        }

        await access.SetGrantedTier(admin, userId, tier, request.Reason);
        return await GetAccount(userId, store, tiers);
    }

    private static async Task<AdminAccountDetail> Detail(AccountAccessRow row, IAccountAccessStore store,
        AccountTierService tiers)
    {
        AccountTier tier = await tiers.GetTier(row.Id);
        List<AccountAccessChange> changes = await store.GetChanges(row.Id);
        return new AdminAccountDetail(ToAccount(row), BillingStatements.TierName(tier),
            changes.Select(change => new AdminAccessChange(change.Id, change.Field,
                    ChangeValue(change.Field, change.OldValue), ChangeValue(change.Field, change.NewValue),
                    change.Reason, change.ChangedByName, change.ChangedAt))
                .ToList());
    }

    /// <summary>Roles are stored by their enum name; the API spells them lowercase like tiers.</summary>
    private static string? ChangeValue(string field, string? value)
    {
        return field == AccessFields.Role ? value?.ToLowerInvariant() : value;
    }

    private static AdminAccount ToAccount(AccountAccessRow row)
    {
        return new AdminAccount(row.Id, row.Username, row.GlobalName, row.AvatarUrl,
            row.Role.ToString().ToLowerInvariant(),
            row.GrantedTier is { } granted ? GrantedTiers.Name(granted) : null,
            row.Identities
                .Select(identity =>
                    new AdminAccountIdentity(identity.Provider, identity.ProviderUserId, identity.Username))
                .ToList());
    }
}
