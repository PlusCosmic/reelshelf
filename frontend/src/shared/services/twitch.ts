import type {
  ImportTwitchClipRequest,
  ImportedTwitchClipResponse,
  TwitchClipSummary,
  TwitchClipsResponse,
  TwitchClipsState,
} from "@/api-client";
import { createTwitchClipsApi } from "./apiClients";

export type { TwitchClipSummary, TwitchClipsResponse, TwitchClipsState };

export type TwitchClipsWindow = 7 | 30 | 90 | null;

export async function fetchTwitchClips(options: {
  cursor?: string | null;
  days?: TwitchClipsWindow;
}): Promise<TwitchClipsResponse> {
  return createTwitchClipsApi().getTwitchClips({
    cursor: options.cursor ?? undefined,
    days: options.days ?? undefined,
  });
}

/** Copies one of the user's Twitch clips into their library; the API reserves storage and pulls the file. */
export async function importTwitchClip(
  request: ImportTwitchClipRequest,
): Promise<ImportedTwitchClipResponse> {
  return createTwitchClipsApi().importTwitchClip({
    importTwitchClipRequest: request,
  });
}
