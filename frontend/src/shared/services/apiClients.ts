import {
  ClipsEndpointsApi,
  ClipSummaryEndpointsApi,
  ClipTranscriptionEndpointsApi,
  Configuration as ClipsConfiguration,
  DiscordActivityEndpointsApi,
  UserEndpointsApi,
  GameCategoryEndpointsApi,
  LegendDetectionEndpointsApi,
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

export function createLegendDetectionApi() {
  return new LegendDetectionEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createClipTranscriptionApi() {
  return new ClipTranscriptionEndpointsApi(
    new ClipsConfiguration(clientOptions),
  );
}

export function createClipSummaryApi() {
  return new ClipSummaryEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createTwitchClipsApi() {
  return new TwitchClipsEndpointsApi(new ClipsConfiguration(clientOptions));
}

export function createDiscordActivityApi(roomToken?: string) {
  return new DiscordActivityEndpointsApi(
    new ClipsConfiguration({
      ...clientOptions,
      // Room endpoints take the Activity's room token, never the site cookie.
      headers: roomToken ? { Authorization: `Bearer ${roomToken}` } : undefined,
    }),
  );
}
