import { useEffect, useRef, useState, type ReactNode } from "react";
import { IconVolume, IconVolume2, IconVolumeOff } from "@tabler/icons-react";
import { activityStreamSource } from "@/shared/services/discordActivity";
import { attachStream } from "@/shared/utils/videoStream";
import {
  loadVolume,
  saveVolume,
  type VolumePreference,
} from "@/shared/utils/volumePreference";
import {
  correctDrift,
  expectedPosition,
  type RoomPlayback,
} from "@/shared/services/watchRoom";

/** How often a follower checks its position against the room. */
const syncIntervalMs = 500;

/** This viewer's own volume, remembered in their browser; it never reaches the room. */
const volumeStorageKey = "reelshelf.activity.volume";

export type HostControls = {
  onPlay: (positionSeconds: number) => void;
  onPause: (positionSeconds: number) => void;
  onSeek: (positionSeconds: number) => void;
  /** The clip reached its end, so the room moves on to the next one. */
  onEnded: (itemId: string) => void;
};

/**
 * Plays the room's clip from its HLS stream, through the Activity's CDN mapping, in a plain `<video>` of its own
 * rather than the site's ClipVideoPlayer, since the room drives it. The host's player is the room's source of
 * truth: its own controls drive playback and each change is sent to the room. Everyone else follows the room,
 * without controls of their own.
 */
export function ActivityPlayer({
  playback,
  host,
  clockOffsetMs,
  overlay,
}: {
  playback: RoomPlayback;
  /** Present when this viewer is the host. */
  host: HostControls | null;
  clockOffsetMs: () => number;
  /** Drawn over the video, such as the room's reactions. */
  overlay?: ReactNode;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [failed, setFailed] = useState(false);
  const [mutedForAutoplay, setMutedForAutoplay] = useState(false);
  const [{ volume, muted }, setVolume] = useState(() =>
    loadVolume(volumeStorageKey),
  );
  const itemId = playback.itemId;
  const {
    host: mediaHost,
    path: mediaPath,
    type: mediaType,
  } = playback.clip.media;
  const isHost = host !== null;

  // The latest props, for media event handlers and the sync loop.
  const playbackRef = useRef(playback);
  const hostRef = useRef(host);
  const offsetRef = useRef(clockOffsetMs);
  useEffect(() => {
    playbackRef.current = playback;
    hostRef.current = host;
    offsetRef.current = clockOffsetMs;
  });

  // Which queue item the element has loaded far enough to report on; host events from a clip being swapped
  // out must not move the new one.
  const loadedItemIdRef = useRef<string | null>(null);
  const syncRef = useRef<() => void>(() => undefined);

  // Volume is personal: applied to this viewer's element only. The browser's autoplay mute sits on top of
  // the viewer's own setting without overwriting it.
  const mutedForAutoplayRef = useRef(mutedForAutoplay);
  useEffect(() => {
    mutedForAutoplayRef.current = mutedForAutoplay;
    const video = videoRef.current;
    if (!video) return;
    video.volume = volume;
    video.muted = muted || mutedForAutoplay;
  }, [volume, muted, mutedForAutoplay]);

  useEffect(
    () => saveVolume(volumeStorageKey, { volume, muted }),
    [volume, muted],
  );

  // The host changes volume with the video's own controls; keep their choice the same way.
  useEffect(() => {
    const video = videoRef.current;
    if (!video || !isHost) return;
    const onVolumeChange = () => {
      if (mutedForAutoplayRef.current) return;
      setVolume({ volume: video.volume, muted: video.muted });
    };
    video.addEventListener("volumechange", onVolumeChange);
    return () => video.removeEventListener("volumechange", onVolumeChange);
  }, [isHost]);

  const changeVolume = (next: VolumePreference) => {
    // Touching the volume is a click, so the browser now allows sound.
    setMutedForAutoplay(false);
    setVolume(next);
  };

  const startPlaying = (video: HTMLVideoElement) => {
    video.play().catch(() => {
      // This viewer hasn't clicked anything yet, so the browser refuses sound: play muted and offer sound.
      video.muted = true;
      setMutedForAutoplay(true);
      void video.play().catch(() => undefined);
    });
  };

  // Keyed by queue item, so the same clip queued again reloads from the start.
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    setFailed(false);
    loadedItemIdRef.current = null;
    const source = activityStreamSource({
      host: mediaHost,
      path: mediaPath,
      type: mediaType,
    });

    // Once the clip is loaded, start where the room is: the beginning for a new clip, or mid-clip for
    // someone joining (or the host reloading) while it plays.
    const onLoaded = () => {
      loadedItemIdRef.current = itemId;
      const room = playbackRef.current;
      video.currentTime = expectedPosition(room, offsetRef.current());
      if (room.playing) startPlaying(video);
    };
    video.addEventListener("loadedmetadata", onLoaded, { once: true });

    const fail = () => setFailed(true);
    // A host the Activity has no URL Mapping for can't play here.
    const detach = attachStream(video, source ? [source] : [], {
      onFatal: fail,
      onUnsupported: fail,
    });
    return () => {
      video.removeEventListener("loadedmetadata", onLoaded);
      detach();
    };
  }, [itemId, mediaHost, mediaPath, mediaType]);

  // The host reports what they do with their own controls.
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const isCurrent = () =>
      loadedItemIdRef.current === playbackRef.current.itemId;
    const report = (kind: "onPlay" | "onPause" | "onSeek") => () => {
      if (isCurrent()) hostRef.current?.[kind](video.currentTime);
    };
    const onPlay = report("onPlay");
    const onPause = report("onPause");
    const onSeeked = report("onSeek");
    const onEnded = () => {
      if (isCurrent()) hostRef.current?.onEnded(playbackRef.current.itemId);
    };
    video.addEventListener("play", onPlay);
    video.addEventListener("pause", onPause);
    video.addEventListener("seeked", onSeeked);
    video.addEventListener("ended", onEnded);
    return () => {
      video.removeEventListener("play", onPlay);
      video.removeEventListener("pause", onPause);
      video.removeEventListener("seeked", onSeeked);
      video.removeEventListener("ended", onEnded);
    };
  }, []);

  // Followers converge on the room's position.
  useEffect(() => {
    const video = videoRef.current;
    if (!video || isHost) {
      syncRef.current = () => undefined;
      return;
    }
    video.playbackRate = 1;

    const sync = () => {
      if (loadedItemIdRef.current !== playbackRef.current.itemId) return;
      const room = playbackRef.current;
      const target = expectedPosition(room, offsetRef.current());

      if (!room.playing) {
        if (!video.paused) video.pause();
        video.playbackRate = 1;
        if (Math.abs(video.currentTime - target) > 0.3) {
          video.currentTime = target;
        }
        return;
      }

      // Finished a moment before the host: wait for the room to move on rather than replay from the start.
      if (video.ended && target >= video.duration - 1) return;

      if (video.paused) {
        video.currentTime = target;
        startPlaying(video);
        return;
      }

      const correction = correctDrift(video.currentTime, target);
      if (correction.kind === "seek") {
        video.playbackRate = 1;
        video.currentTime = correction.to;
      } else {
        video.playbackRate =
          correction.kind === "rate" ? correction.playbackRate : 1;
      }
    };

    syncRef.current = sync;
    const timer = window.setInterval(sync, syncIntervalMs);
    return () => window.clearInterval(timer);
  }, [isHost]);

  // A pause or seek from the host lands at once rather than on the next tick.
  useEffect(() => {
    syncRef.current();
  }, [playback]);

  const silent = muted || mutedForAutoplay || volume === 0;

  return (
    <>
      <div className="rs-activity-player">
        <video
          ref={videoRef}
          className="rs-activity-video"
          controls={isHost}
          playsInline
          aria-label={playback.clip.title}
        />
        {overlay}
        {mutedForAutoplay ? (
          <button
            type="button"
            className="rs-primary rs-activity-unmute"
            onClick={() => changeVolume({ volume: volume || 1, muted: false })}
          >
            Turn sound on
          </button>
        ) : null}
        {failed ? (
          <p className="rs-activity-player-error" role="alert">
            This clip couldn't be played here.
          </p>
        ) : null}
      </div>
      {isHost ? null : (
        <div className="rs-activity-volume">
          <button
            type="button"
            className="rs-icon-button"
            aria-label={silent ? "Unmute" : "Mute"}
            aria-pressed={silent}
            onClick={() =>
              changeVolume(
                silent
                  ? { volume: volume || 1, muted: false }
                  : { volume, muted: true },
              )
            }
          >
            {silent ? (
              <IconVolumeOff size={18} aria-hidden="true" />
            ) : volume < 0.5 ? (
              <IconVolume2 size={18} aria-hidden="true" />
            ) : (
              <IconVolume size={18} aria-hidden="true" />
            )}
          </button>
          <input
            type="range"
            min={0}
            max={1}
            step={0.05}
            aria-label="Volume"
            value={silent ? 0 : volume}
            onChange={(event) => {
              const next = Number(event.target.value);
              changeVolume({ volume: next, muted: next === 0 });
            }}
          />
        </div>
      )}
    </>
  );
}
