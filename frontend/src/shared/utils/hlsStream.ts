/**
 * How a `<video>` plays an HLS playlist: hls.js wherever MediaSource exists, so every browser gets the same
 * automatic quality switching, and the browser's own HLS player where there is no MediaSource but it plays HLS
 * itself (iPhone Safari and iOS webviews, Discord mobile among them). Anything else goes to hls.js, which
 * reports whether it can play there.
 */
export type HlsPlayback = "hls.js" | "native";

export function hlsPlayback(
  video: HTMLVideoElement,
  hasMediaSource = "MediaSource" in window,
): HlsPlayback {
  return !hasMediaSource && video.canPlayType("application/vnd.apple.mpegurl")
    ? "native"
    : "hls.js";
}

/**
 * Attaches an HLS stream to a `<video>` and returns the detach function, which stops loading and frees the
 * hls.js instance. `onFatal` is called when the stream can't be played.
 */
export function attachHlsStream(
  video: HTMLVideoElement,
  source: string,
  onFatal: () => void,
): () => void {
  if (hlsPlayback(video) === "native") {
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
