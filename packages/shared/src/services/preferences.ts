import {
  type UserPreferences,
  type UpdatePreferencesRequest,
  DiscordUserEndpointsApi,
  Configuration,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

// Helper to create API instance with auth
function getPreferencesApi() {
  return new DiscordUserEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
}

/**
 * Fetch current user's preferences
 */
export async function fetchUserPreferences(): Promise<UserPreferences> {
  const api = getPreferencesApi();
  return api.mePreferencesGet().catch((error) => {
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
  const api = getPreferencesApi();
  return api.mePreferencesPatch({ updatePreferencesRequest: request }).catch((error) => {
    console.error('Failed to update user preferences:', { request, error });
    throw error;
  });
}
