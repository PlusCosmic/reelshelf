import type {
  CurrentUserResponse,
  LinkedIdentity,
  StorageUsageResponse,
} from "@/api-client";
import { createUserApi } from "./apiClients";

export interface CurrentUser {
  id: string;
  username: string;
  globalName: string | null;
  avatar: string | null;
  email: string | null;
  suggestedEmail: string | null;
  needsOnboarding: boolean;
}

export async function fetchMe(): Promise<CurrentUser | null> {
  const user = await createUserApi().getMe();
  return user ? fromCurrentUserResponse(user) : null;
}

export type StorageUsage = StorageUsageResponse;

export async function fetchStorageUsage(): Promise<StorageUsage> {
  return createUserApi().getMyStorageUsage();
}

export type { LinkedIdentity };

export async function fetchLinkedIdentities(): Promise<LinkedIdentity[]> {
  return createUserApi().getMyLinkedIdentities();
}

export async function unlinkIdentity(provider: string): Promise<void> {
  await createUserApi().unlinkMyIdentity({ provider });
}

/** Stores the address account mail goes to. An empty string clears it and still completes onboarding. */
export async function setEmail(email: string): Promise<void> {
  await createUserApi().setMyEmail({ setEmailRequest: { email } });
}

function fromCurrentUserResponse(user: CurrentUserResponse): CurrentUser {
  return {
    id: user.id,
    username: user.username,
    globalName: user.globalName ?? null,
    avatar: user.avatar ?? null,
    email: user.email ?? null,
    suggestedEmail: user.suggestedEmail ?? null,
    needsOnboarding: user.needsOnboarding,
  };
}
