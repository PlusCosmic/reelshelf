import { apiConfig } from "../config/apiConfig";
import { ApiError, toApiError } from "./apiError";

export interface UserPreferences {
  discordNotificationsEnabled?: boolean;
}

export interface UpdatePreferencesRequest {
  discordNotificationsEnabled?: boolean;
}

/**
 * Fetch current user's preferences
 */
export async function fetchUserPreferences(): Promise<UserPreferences> {
  return requestPreferences("/api/me/preferences").catch((error: unknown) => {
    console.error('Failed to fetch user preferences:', error);
    // Return default preferences on error
    return { discordNotificationsEnabled: true };
  });
}

/**
 * Update user preferences
 */
export async function updateUserPreferences(
  request: UpdatePreferencesRequest
): Promise<UserPreferences> {
  return requestPreferences("/api/me/preferences", {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  }).catch((error: unknown) => {
    console.error('Failed to update user preferences:', { request, error });
    throw error;
  });
}

async function requestPreferences(
  path: string,
  init?: RequestInit,
): Promise<UserPreferences> {
  const response = await fetch(`${apiConfig.baseUrl}${path}`, {
    credentials: "include",
    ...init,
  });

  if (!response.ok) {
    throw await toApiError(new ApiError(response.statusText, response.status, "unexpected"));
  }

  return response.json();
}
