import { useEffect, useState } from "react";
import { IconMaximize, IconMinimize } from "@tabler/icons-react";
import { activityCopy } from "@/copy/activity";
import { commonCopy } from "@/copy/common";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { Avatar } from "@/components/Reelshelf/ReelshelfPrimitives";
import { defaultCloth } from "@/components/Reelshelf/reelshelf-model";
import { toApiError } from "@/shared/services/apiError";
import { ActivityClipPicker } from "./ActivityClipPicker";
import { ActivityPlayer } from "./ActivityPlayer";
import { ActivityQueue } from "./ActivityQueue";
import {
  ActivityReactionBar,
  ActivityReactionLayer,
} from "./ActivityReactions";
import { useWatchRoom, type WatchRoom } from "./useWatchRoom";
import type { RoomPlayback } from "@/shared/services/watchRoom";
import {
  ActivityUnavailableError,
  activityImageUrl,
  connectToActivity,
  type ActivityConnection,
  type ActivityParticipantResponse,
} from "@/shared/services/discordActivity";

type ConnectState =
  | { status: "connecting" }
  | { status: "connected"; connection: ActivityConnection }
  | { status: "failed"; message: string };

/**
 * The watch room inside a Discord voice channel (ADR-0006): sign in through Discord, then join the room's
 * live connection. Members queue clips from their own shelf; the host controls playback and the queue, and
 * everyone else follows along.
 */
export function ActivityApp() {
  const [state, setState] = useState<ConnectState>({ status: "connecting" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    connectToActivity()
      .then((connection) => {
        if (!cancelled) setState({ status: "connected", connection });
      })
      .catch(async (error: unknown) => {
        const message = await describeFailure(error);
        if (!cancelled) setState({ status: "failed", message });
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  return (
    <main className="rs-activity">
      <header className="rs-activity-header">
        <span className="rs-brand">
          <BrandLogo />
        </span>
        <span className="rs-activity-kicker">{activityCopy.kicker}</span>
      </header>

      {state.status === "connecting" ? (
        <section className="rs-public-state" aria-live="polite">
          <span className="rs-spinner" aria-hidden="true" />
          <p>{activityCopy.joining}</p>
        </section>
      ) : null}

      {state.status === "failed" ? (
        <section className="rs-public-state" aria-live="polite">
          <h1 className="rs-display rs-h2">{state.message}</h1>
          <button
            type="button"
            className="rs-primary"
            onClick={() => {
              setState({ status: "connecting" });
              setAttempt((value) => value + 1);
            }}
          >
            {commonCopy.tryAgain}
          </button>
        </section>
      ) : null}

      {state.status === "connected" ? (
        <ActivityRoom connection={state.connection} />
      ) : null}
    </main>
  );
}

function ActivityRoom({ connection }: { connection: ActivityConnection }) {
  const { session } = connection;
  const { participant } = session;
  const room = useWatchRoom(session);
  const playback = room.state?.playback ?? null;
  const host = room.state?.participants.find((person) => person.isHost);
  const queue = room.state?.queue ?? [];
  const canQueue =
    participant.isMember && (room.isHost || !room.state?.queueLocked);

  return (
    <section className="rs-activity-room">
      {room.status !== "connected" ? (
        <p className="rs-activity-banner" aria-live="polite">
          {room.status === "connecting"
            ? activityCopy.room.connecting
            : activityCopy.room.reconnecting}
        </p>
      ) : null}

      {playback ? <ActivityNowPlaying room={room} playback={playback} /> : null}

      {room.error ? (
        <p className="rs-activity-banner" role="alert">
          {room.error}
        </p>
      ) : null}

      <div className="rs-activity-you">
        <ParticipantAvatar person={participant} size={56} />
        <div>
          <h1 className="rs-display rs-h2">{participant.name}</h1>
          <p className="rs-activity-role">
            {describeRole(room, participant, host?.name)}
          </p>
        </div>
      </div>

      <div className="rs-activity-present">
        <h2 className="rs-activity-kicker">{activityCopy.room.present}</h2>
        <ul>
          {(room.state?.participants ?? []).map((person) => (
            <li key={person.discordUserId}>
              <ParticipantAvatar person={person} size={28} />
              <span>{person.name}</span>
              {person.isHost ? (
                <span className="rs-activity-tag">
                  {activityCopy.room.host}
                </span>
              ) : !person.isMember ? (
                <span className="rs-activity-tag is-quiet">
                  {activityCopy.room.guest}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      </div>

      {playback || queue.length > 0 || participant.isMember ? (
        <ActivityQueue
          room={room}
          viewerDiscordUserId={participant.discordUserId}
        />
      ) : null}

      {canQueue ? (
        <ActivityClipPicker
          roomToken={session.roomToken}
          queuedClipIds={new Set(queue.map((item) => item.clipId))}
          onPick={(clip) => room.addToQueue(clip.clipId)}
        />
      ) : null}
    </section>
  );
}

/**
 * The playing clip with its details and reactions. Expanding it fills the whole Activity window with the
 * video; it collapses again on the toggle or Escape, and whenever the room stops playing.
 */
function ActivityNowPlaying({
  room,
  playback,
}: {
  room: WatchRoom;
  playback: RoomPlayback;
}) {
  const [expanded, setExpanded] = useState(false);
  const queue = room.state?.queue ?? [];

  useEffect(() => {
    if (!expanded) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setExpanded(false);
    };
    // The room underneath must not scroll while the video covers it.
    const root = document.documentElement;
    const previousOverflow = root.style.overflow;
    root.style.overflow = "hidden";
    window.addEventListener("keydown", onKeyDown);
    return () => {
      root.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [expanded]);

  return (
    <div className={`rs-activity-now${expanded ? " is-expanded" : ""}`}>
      <ActivityPlayer
        playback={playback}
        clockOffsetMs={room.clockOffsetMs}
        overlay={
          <>
            <ActivityReactionLayer
              reactions={room.reactions}
              itemId={playback.itemId}
            />
            <button
              type="button"
              className="rs-icon-button rs-activity-expand"
              aria-label={
                expanded
                  ? activityCopy.nowPlaying.exitFullView
                  : activityCopy.nowPlaying.fullView
              }
              aria-pressed={expanded}
              title={
                expanded
                  ? activityCopy.nowPlaying.exitFullViewHint
                  : activityCopy.nowPlaying.fullView
              }
              onClick={() => setExpanded((value) => !value)}
            >
              {expanded ? (
                <IconMinimize size={18} aria-hidden="true" />
              ) : (
                <IconMaximize size={18} aria-hidden="true" />
              )}
            </button>
          </>
        }
        host={
          room.isHost
            ? {
                onPlay: room.play,
                onPause: room.pause,
                onSeek: room.seek,
                onEnded: room.next,
              }
            : null
        }
      />
      <div className="rs-activity-now-details">
        <div>
          <h2 className="rs-display rs-activity-now-title">
            {playback.clip.title}
          </h2>
          <p className="rs-activity-role">
            {[
              playback.clip.game,
              activityCopy.nowPlaying.fromShelf(playback.clip.ownerName),
            ]
              .filter(Boolean)
              .join(" · ")}
          </p>
        </div>
        {room.isHost ? (
          <button
            type="button"
            className="rs-primary"
            onClick={() => room.next(playback.itemId)}
          >
            {queue.length > 0
              ? activityCopy.nowPlaying.skip
              : activityCopy.nowPlaying.stop}
          </button>
        ) : null}
      </div>
      <ActivityReactionBar onReact={room.react} />
    </div>
  );
}

/** Discord picture when there is one, else their initial on a cloth colour that stays theirs across rooms. */
function ParticipantAvatar({
  person,
  size,
}: {
  person: { discordUserId: string; name: string; avatarUrl?: string | null };
  size: number;
}) {
  return (
    <Avatar
      name={person.name}
      src={activityImageUrl(person.avatarUrl)}
      size={size}
      color={defaultCloth(person.discordUserId)}
    />
  );
}

function describeRole(
  room: WatchRoom,
  participant: ActivityParticipantResponse,
  hostName: string | undefined,
): string {
  if (room.isHost) {
    return activityCopy.role.hosting;
  }
  const hosting = hostName
    ? activityCopy.role.hostIs(hostName)
    : activityCopy.role.noHost;
  if (!participant.isMember) {
    return activityCopy.role.guest(hosting);
  }
  return room.state?.queueLocked
    ? activityCopy.role.locked(hosting)
    : activityCopy.role.member(hosting);
}

async function describeFailure(error: unknown): Promise<string> {
  if (error instanceof ActivityUnavailableError) return error.message;

  const apiError = await toApiError(error);
  if (apiError.status === 403) {
    return activityCopy.failure.notInInstance;
  }
  if (apiError.status === 503) return activityCopy.failure.switchedOff;
  return activityCopy.failure.couldNotJoin;
}
