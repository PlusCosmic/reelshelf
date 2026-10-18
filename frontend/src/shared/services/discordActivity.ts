import { DiscordSDK } from "@discord/embedded-app-sdk";
import type {
  ActivityClip,
  ActivityClipsResponse,
  ActivityParticipantResponse,
  ActivitySessionResponse,
} from "@/api-client";
import { createDiscordActivityApi } from "./apiClients";

export type {
  ActivityClip,
  ActivityClipsResponse,
  ActivityParticipantResponse,
  ActivitySessionResponse,
};

/**
 * Discord's CSP only lets the Activity load from its proxy origin, so Discord CDN images go through the
 * `/discord-cdn` URL Mapping (→ cdn.discordapp.com) set on the Discord application.
 */
export function activityImageUrl(
  url: string | null | undefined,
): string | null {
  if (!url) return null;
  return url.replace(/^https:\/\/cdn\.discordapp\.com\//, "/discord-cdn/");
}

/** Each video host's media reaches the Activity through its own URL Mapping; see `clipMediaUrls.ts`. */
export {
  activityStreamSource,
  activityThumbnailUrl,
} from "@/shared/utils/clipMediaUrls";

/** A room member's own clips, newest first; guests are refused with a 403. */
export async function fetchOwnClips(
  roomToken: string,
  options: { search?: string; page?: number },
): Promise<ActivityClipsResponse> {
  return createDiscordActivityApi(roomToken).getActivityClips({
    search: options.search || undefined,
    page: options.page ?? 1,
  });
}

export type ActivityConnection = {
  sdk: DiscordSDK;
  session: ActivitySessionResponse;
};

export class ActivityUnavailableError extends Error {
  constructor() {
    super("The watch room isn't switched on yet.");
    this.name = "ActivityUnavailableError";
  }
}

/**
 * The Embedded App SDK handshake: wait for Discord, ask the user to authorize (silently after the first
 * time), trade the code for a room token at the API, then authenticate the SDK with the Discord token.
 */
export async function connectToActivity(): Promise<ActivityConnection> {
  const api = createDiscordActivityApi();
  const config = await api.getActivityConfig();
  if (!config.enabled || !config.clientId) {
    throw new ActivityUnavailableError();
  }

  const sdk = new DiscordSDK(config.clientId);
  await sdk.ready();

  const { code } = await sdk.commands.authorize({
    client_id: config.clientId,
    response_type: "code",
    state: "",
    prompt: "none",
    scope: ["identify"],
  });

  const session = await api.exchangeActivityToken({
    activityTokenRequest: { code, instanceId: sdk.instanceId },
  });
  await sdk.commands.authenticate({ access_token: session.accessToken });

  return { sdk, session };
}
