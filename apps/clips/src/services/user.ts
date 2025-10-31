import {
  Configuration,
  type DiscordUser,
  DiscordUserEndpointsApi,
} from "@pluscosmic/nucleus-api-client";
import { apiConfig } from "../config/apiConfig.ts";

export async function fetchMe(): Promise<DiscordUser | null> {
  const api = new DiscordUserEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.meGet().catch(() => null);
}
