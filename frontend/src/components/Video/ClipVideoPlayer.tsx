import { useEffect, useRef, useState } from "react";
import type { PlaybackSource } from "@/api-client";
import { attachStream } from "@/shared/utils/videoStream";
import { loadVolume, saveVolume } from "@/shared/utils/volumePreference";

/** The viewer's volume for every player on the site, so it carries over from clip to clip. */
const volumeStorageKey = "reelshelf.player.volume";

/** Identifies a list of sources by value, so a re-fetched clip with the same sources keeps playing. */
function sourcesKey(sources: readonly PlaybackSource[]) {
  return JSON.stringify(
    sources.map(({ url, type, height }) => [url, type, height ?? null]),
  );
}

type Problem = { key: string; kind: "fatal" | "unsupported" };

/**
 * Plays a clip in a `<video>` with the browser's own controls and fullscreen, from the first of its sources the
 * device can play (`attachStream`): a progressive MP4 as the video's own `src`, or HLS with hls.js picking the
 * quality. It fills its container, which must be positioned, and stays transparent until the first frame
 * arrives so whatever is behind it (the poster, a thumbnail) shows while it loads.
 *
 * New `sources` swap the stream on the same element, so moving to the next clip keeps the browser's permission
 * to play with sound. The volume and mute set on its controls are remembered in the browser and applied to every
 * player, so they carry over to the next clip.
 */
export function ClipVideoPlayer({
  sources,
  poster,
  title,
  autoPlay = false,
  className,
  onLoaded,
  onEnded,
}: {
  /** The clip's sources as the API handed them out, in the order to try them. */
  sources: readonly PlaybackSource[];
  poster?: string;
  title: string;
  /**
   * Start playing once loaded. A browser that won't allow it (sound without a click first) leaves the clip
   * paused on its controls.
   */
  autoPlay?: boolean;
  className?: string;
  /** Called once per set of sources, when the browser knows the clip's length and size. */
  onLoaded?: () => void;
  onEnded?: () => void;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const key = sourcesKey(sources);
  const [loadedKey, setLoadedKey] = useState<string | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);

  // The latest props, for media event handlers and for the stream effect, which is keyed by value.
  const sourcesRef = useRef(sources);
  const onLoadedRef = useRef(onLoaded);
  const onEndedRef = useRef(onEnded);
  useEffect(() => {
    sourcesRef.current = sources;
    onLoadedRef.current = onLoaded;
    onEndedRef.current = onEnded;
  });

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const onMetadata = () => onLoadedRef.current?.();
    const onFirstFrame = () => setLoadedKey(key);
    video.addEventListener("loadedmetadata", onMetadata, { once: true });
    video.addEventListener("loadeddata", onFirstFrame, { once: true });
    const detach = attachStream(video, sourcesRef.current, {
      onFatal: () => setProblem({ key, kind: "fatal" }),
      onUnsupported: () => setProblem({ key, kind: "unsupported" }),
    });
    return () => {
      video.removeEventListener("loadedmetadata", onMetadata);
      video.removeEventListener("loadeddata", onFirstFrame);
      detach();
    };
  }, [key]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const ended = () => onEndedRef.current?.();
    video.addEventListener("ended", ended);
    return () => video.removeEventListener("ended", ended);
  }, []);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const { volume, muted } = loadVolume(volumeStorageKey);
    video.volume = volume;
    video.muted = muted;
    const onVolumeChange = () =>
      saveVolume(volumeStorageKey, {
        volume: video.volume,
        muted: video.muted,
      });
    video.addEventListener("volumechange", onVolumeChange);
    return () => video.removeEventListener("volumechange", onVolumeChange);
  }, []);

  const currentProblem = problem?.key === key ? problem.kind : null;

  return (
    <>
      <video
        ref={videoRef}
        className={className}
        poster={poster}
        autoPlay={autoPlay}
        controls
        playsInline
        aria-label={title}
        data-loaded={loadedKey === key ? "" : undefined}
      />
      {currentProblem === "unsupported" ? (
        // Where a fallback (an H.264 copy made on demand) would plug in.
        <p className="rs-video-error" role="alert">
          This clip can't play on this device.
        </p>
      ) : currentProblem === "fatal" ? (
        <p className="rs-video-error" role="alert">
          This clip couldn't be played.
        </p>
      ) : null}
    </>
  );
}
