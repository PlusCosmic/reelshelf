import {
  ClipsEndpointsApi,
  Configuration as ClipsConfiguration,
  GameCategoryEndpointsApi,
  PlaylistEndpointsApi,
} from "@repo/clips-api-client";
import {
  Configuration as MinecraftConfiguration,
  MinecraftEndpointsApi,
} from "@repo/minecraft-api-client";
import { apiConfig } from "../config/apiConfig";
import { createApiErrorMiddleware } from "./apiError";

const clientOptions = {
  basePath: apiConfig.baseUrl,
  credentials: "include" as const,
  middleware: [createApiErrorMiddleware()],
};

export function createClipsApi() {
  return new ClipsEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createGameCategoryApi() {
  return new GameCategoryEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createPlaylistApi() {
  return new PlaylistEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createMinecraftApi() {
  return new MinecraftEndpointsApi(new MinecraftConfiguration(clientOptions));
}
