import { useCallback, useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import type { ActivitySessionResponse } from "@/shared/services/discordActivity";
import {
  createWatchRoomConnection,
  isConnected,
  refreshRoomToken,
  type RoomReaction,
  type RoomStateView,
} from "@/shared/services/watchRoom";

export type WatchRoomStatus = "connecting" | "connected" | "reconnecting";

/** How long a reaction stays on screen; matches the float animation in reelshelf.css. */
const reactionLifetimeMs = 2600;

/** More than this on screen at once and the oldest make way. */
const maxReactionsOnScreen = 30;

/** A reaction on screen, with a lane across the player so a burst spreads out. */
export type FloatingReaction = RoomReaction & { key: number; lane: number };

/** Renew the room token this long before it expires, so a reconnect never presents an expired one. */
const refreshLeadMs = 5 * 60_000;

export type WatchRoom = {
  state: RoomStateView | null;
  status: WatchRoomStatus;
  isHost: boolean;
  /** Server time minus local time, from the latest room state. */
  clockOffsetMs: () => number;
  error: string | null;
  reactions: FloatingReaction[];
  react: (emoji: string) => void;
  addToQueue: (clipId: string) => void;
  removeFromQueue: (itemId: string) => void;
  moveInQueue: (itemId: string, toIndex: number) => void;
  lockQueue: (locked: boolean) => void;
  playNow: (itemId: string) => void;
  /** Moves on from `currentItemId`; with null, from whatever is playing. */
  next: (currentItemId: string | null) => void;
  play: (positionSeconds: number) => void;
  pause: (positionSeconds: number) => void;
  seek: (positionSeconds: number) => void;
};

/** The live connection to this Activity instance's watch room. */
export function useWatchRoom(session: ActivitySessionResponse): WatchRoom {
  const [state, setState] = useState<RoomStateView | null>(null);
  const [status, setStatus] = useState<WatchRoomStatus>("connecting");
  const [error, setError] = useState<string | null>(null);
  const [reactions, setReactions] = useState<FloatingReaction[]>([]);
  const tokenRef = useRef(session.roomToken);
  const expiresAtRef = useRef(session.roomTokenExpiresAt);
  const offsetRef = useRef(0);
  const versionRef = useRef(-1);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let disposed = false;
    let refreshTimer: number | undefined;
    let reactionCount = 0;
    const reactionTimers = new Set<number>();

    const scheduleRefresh = () => {
      const delay = Math.max(
        10_000,
        expiresAtRef.current.getTime() - Date.now() - refreshLeadMs,
      );
      refreshTimer = window.setTimeout(() => {
        refreshRoomToken(tokenRef.current)
          .then((refreshed) => {
            tokenRef.current = refreshed.roomToken;
            expiresAtRef.current = refreshed.roomTokenExpiresAt;
          })
          .catch(() => undefined)
          .finally(() => {
            if (!disposed) scheduleRefresh();
          });
      }, delay);
    };
    scheduleRefresh();

    const connection = createWatchRoomConnection(() => tokenRef.current);
    connectionRef.current = connection;

    connection.on("RoomState", (next: RoomStateView) => {
      // States can overtake each other; keep the newest. A rejoined room after a restart counts from zero
      // again, which is why a reconnect resets the version.
      if (next.version <= versionRef.current) return;
      versionRef.current = next.version;
      offsetRef.current = Date.parse(next.serverTime) - Date.now();
      setState(next);
    });
    connection.on("Reaction", (reaction: RoomReaction) => {
      const key = reactionCount++;
      setReactions((current) =>
        [...current, { ...reaction, key, lane: key % 8 }].slice(
          -maxReactionsOnScreen,
        ),
      );
      const timer = window.setTimeout(() => {
        reactionTimers.delete(timer);
        setReactions((current) => current.filter((item) => item.key !== key));
      }, reactionLifetimeMs);
      reactionTimers.add(timer);
    });
    connection.onreconnecting(() => {
      if (!disposed) setStatus("reconnecting");
    });
    connection.onreconnected(() => {
      versionRef.current = -1;
      if (!disposed) setStatus("connected");
    });

    connection
      .start()
      .then(() => {
        if (!disposed) setStatus("connected");
      })
      .catch(() => {
        if (!disposed) setStatus("reconnecting");
      });

    return () => {
      disposed = true;
      window.clearTimeout(refreshTimer);
      reactionTimers.forEach((timer) => window.clearTimeout(timer));
      connectionRef.current = null;
      void connection.stop();
    };
  }, []);

  const invoke = useCallback((method: string, ...args: unknown[]) => {
    const connection = connectionRef.current;
    if (!connection || !isConnected(connection)) return;
    setError(null);
    connection.invoke(method, ...args).catch((failure: unknown) => {
      // HubException messages arrive prefixed with SignalR's own wording.
      const message =
        failure instanceof Error
          ? failure.message.replace(/^.*HubException: /, "")
          : "Something went wrong.";
      setError(message);
    });
  }, []);

  return {
    state,
    status,
    isHost: state?.hostDiscordUserId === session.participant.discordUserId,
    clockOffsetMs: () => offsetRef.current,
    error,
    reactions,
    react: (emoji) => invoke("React", emoji),
    addToQueue: (clipId) => invoke("AddToQueue", clipId),
    removeFromQueue: (itemId) => invoke("RemoveFromQueue", itemId),
    moveInQueue: (itemId, toIndex) => invoke("MoveInQueue", itemId, toIndex),
    lockQueue: (locked) => invoke("LockQueue", locked),
    playNow: (itemId) => invoke("PlayNow", itemId),
    next: (currentItemId) => invoke("Next", currentItemId),
    play: (position) => invoke("Play", position),
    pause: (position) => invoke("Pause", position),
    seek: (position) => invoke("Seek", position),
  };
}
