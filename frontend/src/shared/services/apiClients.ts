import {
  ClipsEndpointsApi,
  Configuration as ClipsConfiguration,
  UserEndpointsApi,
  GameCategoryEndpointsApi,
  PlaylistEndpointsApi,
  SharedClipsEndpointsApi,
  TwitchClipsEndpointsApi,
} from "@/api-client";
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

export function createSharedClipsApi() {
  return new SharedClipsEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createGameCategoryApi() {
  return new GameCategoryEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createUserApi() {
  return new UserEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createPlaylistApi() {
  return new PlaylistEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createTwitchClipsApi() {
  return new TwitchClipsEndpointsApi(new ClipsConfiguration(clientOptions));
}
