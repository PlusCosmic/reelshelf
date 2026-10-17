import { useEffect, useRef, useState } from "react";
import { attachHlsStream } from "@/shared/utils/hlsStream";

/**
 * Plays a clip's HLS stream in a `<video>` with the browser's own controls and fullscreen; hls.js picks the
 * quality. It fills its container, which must be positioned, and stays transparent until the first frame
 * arrives so whatever is behind it (the poster, a thumbnail) shows while it loads.
 *
 * A new `src` swaps the stream on the same element, so moving to the next clip keeps the browser's permission
 * to play with sound.
 */
export function ClipVideoPlayer({
  src,
  poster,
  title,
  autoPlay = false,
  className,
  onLoaded,
  onEnded,
}: {
  /** The HLS playlist the API handed out. */
  src: string;
  poster?: string;
  title: string;
  /**
   * Start playing once loaded. A browser that won't allow it (sound without a click first) leaves the clip
   * paused on its controls.
   */
  autoPlay?: boolean;
  className?: string;
  /** Called once per `src`, when the browser knows the clip's length and size. */
  onLoaded?: () => void;
  onEnded?: () => void;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [loadedSrc, setLoadedSrc] = useState<string | null>(null);
  const [failedSrc, setFailedSrc] = useState<string | null>(null);

  // The latest callbacks, for media event handlers.
  const onLoadedRef = useRef(onLoaded);
  const onEndedRef = useRef(onEnded);
  useEffect(() => {
    onLoadedRef.current = onLoaded;
    onEndedRef.current = onEnded;
  });

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const onMetadata = () => onLoadedRef.current?.();
    const onFirstFrame = () => setLoadedSrc(src);
    video.addEventListener("loadedmetadata", onMetadata, { once: true });
    video.addEventListener("loadeddata", onFirstFrame, { once: true });
    const detach = attachHlsStream(video, src, () => setFailedSrc(src));
    return () => {
      video.removeEventListener("loadedmetadata", onMetadata);
      video.removeEventListener("loadeddata", onFirstFrame);
      detach();
    };
  }, [src]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const ended = () => onEndedRef.current?.();
    video.addEventListener("ended", ended);
    return () => video.removeEventListener("ended", ended);
  }, []);

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
        data-loaded={loadedSrc === src ? "" : undefined}
      />
      {failedSrc === src ? (
        <p className="rs-video-error" role="alert">
          This clip couldn't be played.
        </p>
      ) : null}
    </>
  );
}
