import type { LinkedIdentity, StorageUsageResponse } from "@/api-client";
import { apiConfig } from "../config/apiConfig";
import { createUserApi } from "./apiClients";
import { ApiError, toApiError } from "./apiError";

export interface CurrentUser {
  id: string;
  username: string;
  globalName: string | null;
  avatar: string | null;
}

export async function fetchMe(): Promise<CurrentUser | null> {
  return requestUser("/api/me");
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

interface CurrentUserResponse {
  id: string;
  username: string;
  global_name?: string | null;
  globalName?: string | null;
  avatar: string | null;
}

async function requestUser(path: string): Promise<CurrentUser | null> {
  const user = await requestJson<CurrentUserResponse | null>(path);
  return user ? fromCurrentUserResponse(user) : null;
}

async function requestJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiConfig.baseUrl}${path}`, {
    credentials: "include",
  });

  if (!response.ok) {
    throw await toApiError(
      new ApiError(response.statusText, response.status, "unexpected"),
    );
  }

  return response.json();
}

function fromCurrentUserResponse(user: CurrentUserResponse): CurrentUser {
  return {
    id: user.id,
    username: user.username,
    globalName: user.global_name ?? user.globalName ?? null,
    avatar: user.avatar,
  };
}
