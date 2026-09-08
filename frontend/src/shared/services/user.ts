import type { StorageUsageResponse } from "@/api-client";
import { apiConfig } from "../config/apiConfig";
import { createUserApi } from "./apiClients";
import { ApiError, toApiError } from "./apiError";

export interface DiscordUser {
  id: string;
  username: string;
  globalName: string | null;
  avatar: string | null;
}

export async function fetchMe(): Promise<DiscordUser | null> {
  return requestUser("/api/me");
}

export type StorageUsage = StorageUsageResponse;

export async function fetchStorageUsage(): Promise<StorageUsage> {
  return createUserApi().getMyStorageUsage();
}

interface DiscordUserResponse {
  id: string;
  username: string;
  global_name?: string | null;
  globalName?: string | null;
  avatar: string | null;
}

async function requestUser(path: string): Promise<DiscordUser | null> {
  const user = await requestJson<DiscordUserResponse | null>(path);
  return user ? fromDiscordUserResponse(user) : null;
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

function fromDiscordUserResponse(user: DiscordUserResponse): DiscordUser {
  return {
    id: user.id,
    username: user.username,
    globalName: user.global_name ?? user.globalName ?? null,
    avatar: user.avatar,
  };
}
