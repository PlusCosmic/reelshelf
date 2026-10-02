import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import type { RoomTokenResponse } from "@/api-client";
import { createDiscordActivityApi } from "./apiClients";

// The hub speaks SignalR's camelCase JSON (see WatchRoomHub), so these mirror the C# records by hand.
export type NowPlaying = {
  clipId: string;
  videoId: string;
  title: string;
  game: string;
  ownerName: string;
  durationSeconds: number;
};

export type RoomPlayback = {
  /** This play of the clip; a clip queued again later is a new item. */
  itemId: string;
  clip: NowPlaying;
  ownerDiscordUserId: string;
  playing: boolean;
  positionSeconds: number;
  /** Server time when `positionSeconds` was recorded. */
  updatedAt: string;
};

export type RoomParticipantView = {
  discordUserId: string;
  name: string;
  avatarUrl: string | null;
  isMember: boolean;
  isHost: boolean;
};

/** A queued clip. It has no video id: the stream is only handed out once the clip plays. */
export type QueueItemView = {
  itemId: string;
  clipId: string;
  title: string;
  game: string;
  ownerName: string;
  ownerDiscordUserId: string;
  durationSeconds: number;
};

export type RoomStateView = {
  version: number;
  hostDiscordUserId: string | null;
  participants: RoomParticipantView[];
  playback: RoomPlayback | null;
  queue: QueueItemView[];
  queueLocked: boolean;
  serverTime: string;
};

/** An emoji someone sent while a clip played; broadcast once, never kept. */
export type RoomReaction = {
  itemId: string;
  discordUserId: string;
  name: string;
  emoji: string;
};

/** The reactions on offer; the hub refuses any other (WatchRoom.ReactionEmoji). */
export const reactionEmoji = ["😂", "🔥", "😮", "👏", "💀", "❤️"] as const;

export const watchRoomHubPath = "/api/activity/hub";

/**
 * A hub connection that keeps retrying: a deploy restarts the API and drops every room, and the room
 * should come back by itself rather than after four attempts.
 */
export function createWatchRoomConnection(
  getToken: () => string,
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(watchRoomHubPath, { accessTokenFactory: getToken })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
        Math.min(30_000, 1_000 * 2 ** previousRetryCount),
    })
    .configureLogging(LogLevel.Warning)
    .build();
}

export function isConnected(connection: HubConnection): boolean {
  return connection.state === HubConnectionState.Connected;
}

export async function refreshRoomToken(
  roomToken: string,
): Promise<RoomTokenResponse> {
  return createDiscordActivityApi(roomToken).refreshActivityToken();
}

/**
 * Where playback should be now, in seconds. `clockOffsetMs` is server time minus local time, so a client
 * whose clock is off still lands where everyone else is.
 */
export function expectedPosition(
  playback: RoomPlayback,
  clockOffsetMs: number,
  nowMs = Date.now(),
): number {
  if (!playback.playing) return playback.positionSeconds;

  const elapsed =
    (nowMs + clockOffsetMs - Date.parse(playback.updatedAt)) / 1000;
  const position = playback.positionSeconds + Math.max(0, elapsed);
  const duration = playback.clip.durationSeconds;
  return duration > 0 ? Math.min(position, duration) : position;
}

export type DriftCorrection =
  | { kind: "none" }
  | { kind: "rate"; playbackRate: number }
  | { kind: "seek"; to: number };

/**
 * How a follower closes the gap to `target`: leave small drift alone, nudge the playback rate for a
 * moderate gap (no visible jump), and seek when it is too far to catch up smoothly.
 */
export function correctDrift(current: number, target: number): DriftCorrection {
  const behind = target - current;
  const gap = Math.abs(behind);
  if (gap > 1) return { kind: "seek", to: target };
  if (gap > 0.15) {
    const nudge = Math.max(-0.1, Math.min(0.1, behind * 0.5));
    return { kind: "rate", playbackRate: 1 + nudge };
  }
  return { kind: "none" };
}
