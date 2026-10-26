import type {
  AdminAccessChange,
  AdminAccount,
  AdminAccountDetail,
  AdminAccountIdentity,
} from "@/api-client";
import { createAdminAccountApi } from "./apiClients";

export type {
  AdminAccessChange,
  AdminAccount,
  AdminAccountDetail,
  AdminAccountIdentity,
};

export const accountRoles = ["viewer", "editor", "admin"] as const;
export type AccountRole = (typeof accountRoles)[number];

/** Tiers an admin can grant; null is no grant. */
export const grantableTiers = ["plus", "pro", "unlimited"] as const;
export type GrantableTier = (typeof grantableTiers)[number];

/** Accounts matching a username, display name or exact Discord or Twitch id. */
export async function findAccounts(query: string): Promise<AdminAccount[]> {
  return createAdminAccountApi().findAdminAccounts({ query });
}

export async function fetchAccount(
  userId: string,
): Promise<AdminAccountDetail> {
  return createAdminAccountApi().getAdminAccount({ userId });
}

export async function setAccountRole(
  userId: string,
  role: AccountRole,
  reason: string,
): Promise<AdminAccountDetail> {
  return createAdminAccountApi().setAdminAccountRole({
    userId,
    setAccountRoleRequest: { role, reason },
  });
}

export async function setGrantedTier(
  userId: string,
  tier: GrantableTier | null,
  reason: string,
): Promise<AdminAccountDetail> {
  return createAdminAccountApi().setAdminAccountGrantedTier({
    userId,
    setGrantedTierRequest: { tier, reason },
  });
}
