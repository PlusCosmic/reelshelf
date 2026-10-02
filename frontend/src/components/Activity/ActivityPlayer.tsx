import { useEffect, useRef, useState } from "react";
import { activityStreamUrl } from "@/shared/services/discordActivity";
import {
  correctDrift,
  expectedPosition,
  type RoomPlayback,
} from "@/shared/services/watchRoom";

/** How often a follower checks its position against the room. */
const syncIntervalMs = 500;

export type HostControls = {
  onPlay: (positionSeconds: number) => void;
  onPause: (positionSeconds: number) => void;
  onSeek: (positionSeconds: number) => void;
  /** The clip reached its end, so the room moves on to the next one. */
  onEnded: (itemId: string) => void;
};

/**
 * Plays the room's clip from Bunny's HLS stream in a plain `<video>`; the Activity cannot nest Bunny's embed
 * player. The host's player is the room's source of truth: its own controls drive playback and each change is
 * sent to the room. Everyone else follows the room, without controls of their own.
 */
export function ActivityPlayer({
  playback,
  host,
  clockOffsetMs,
}: {
  playback: RoomPlayback;
  /** Present when this viewer is the host. */
  host: HostControls | null;
  clockOffsetMs: () => number;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [failed, setFailed] = useState(false);
  const [mutedForAutoplay, setMutedForAutoplay] = useState(false);
  const itemId = playback.itemId;
  const videoId = playback.clip.videoId;
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
    const source = activityStreamUrl(videoId);

    // Once the clip is loaded, start where the room is: the beginning for a new clip, or mid-clip for
    // someone joining (or the host reloading) while it plays.
    const onLoaded = () => {
      loadedItemIdRef.current = itemId;
      const room = playbackRef.current;
      video.currentTime = expectedPosition(room, offsetRef.current());
      if (room.playing) startPlaying(video);
    };
    video.addEventListener("loadedmetadata", onLoaded, { once: true });

    const detach = attachStream(video, source, () => setFailed(true));
    return () => {
      video.removeEventListener("loadedmetadata", onLoaded);
      detach();
    };
  }, [itemId, videoId]);

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

  return (
    <div className="rs-activity-player">
      <video
        ref={videoRef}
        className="rs-activity-video"
        controls={isHost}
        playsInline
        aria-label={playback.clip.title}
      />
      {mutedForAutoplay ? (
        <button
          type="button"
          className="rs-primary rs-activity-unmute"
          onClick={() => {
            const video = videoRef.current;
            if (video) video.muted = false;
            setMutedForAutoplay(false);
          }}
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
  );
}

/**
 * Attaches an HLS stream: hls.js where MediaSource exists, the native player on iOS webviews (Discord
 * mobile). Returns the detach function.
 */
function attachStream(
  video: HTMLVideoElement,
  source: string,
  onFatal: () => void,
): () => void {
  if (
    !("MediaSource" in window) &&
    video.canPlayType("application/vnd.apple.mpegurl")
  ) {
    video.addEventListener("error", onFatal);
    video.src = source;
    return () => {
      video.removeEventListener("error", onFatal);
      video.removeAttribute("src");
      video.load();
    };
  }

  let cancelled = false;
  let destroy: (() => void) | undefined;
  // Loaded on first play, and the light build: clips need no subtitles, alternate audio or DRM.
  void import("hls.js/light").then(({ default: Hls }) => {
    if (cancelled) return;
    if (!Hls.isSupported()) {
      onFatal();
      return;
    }
    const hls = new Hls();
    hls.on(Hls.Events.ERROR, (_event, data) => {
      if (data.fatal) onFatal();
    });
    hls.loadSource(source);
    hls.attachMedia(video);
    destroy = () => hls.destroy();
  });

  return () => {
    cancelled = true;
    destroy?.();
  };
}
