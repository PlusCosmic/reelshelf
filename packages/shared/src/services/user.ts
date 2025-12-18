import {
  Configuration,
  type DiscordUser,
  DiscordUserEndpointsApi,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

export async function fetchMe(): Promise<DiscordUser | null> {
  const api = new DiscordUserEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.meGet().catch(() => null);
}

export async function fetchUser(userId : string): Promise<DiscordUser | null> {
  const api = new DiscordUserEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.userUserIdGet({ userId: userId}).catch(() => null);
}

/**
 * Fetch suggested users (Discord friends) for collaborator selection
 */
export async function fetchUserSuggestions(): Promise<DiscordUser[]> {
  const api = new DiscordUserEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.usersSuggestionsGet().catch((error) => {
    console.error('Failed to fetch user suggestions:', error);
    return [];
  });
}
