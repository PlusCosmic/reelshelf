import { useEffect, useRef, useState } from "react";
import { activityStreamUrl } from "@/shared/services/discordActivity";

/**
 * Plays a clip's Bunny HLS stream in a plain `<video>`. The Activity cannot nest Bunny's embed player, and
 * owning the element is what later lets the room keep everyone's position in step.
 */
export function ActivityPlayer({
  videoId,
  title,
}: {
  videoId: string;
  title: string;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    setFailed(false);
    const source = activityStreamUrl(videoId);
    // The pick was a click, so the browser usually allows playback with sound; if not, the controls remain.
    const start = () => void video.play().catch(() => undefined);

    // iOS webviews (Discord mobile) have no MediaSource but play HLS natively.
    if (
      !("MediaSource" in window) &&
      video.canPlayType("application/vnd.apple.mpegurl")
    ) {
      const onError = () => setFailed(true);
      video.addEventListener("error", onError);
      video.src = source;
      start();
      return () => {
        video.removeEventListener("error", onError);
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
        setFailed(true);
        return;
      }
      const hls = new Hls();
      hls.on(Hls.Events.MANIFEST_PARSED, start);
      hls.on(Hls.Events.ERROR, (_event, data) => {
        if (data.fatal) setFailed(true);
      });
      hls.loadSource(source);
      hls.attachMedia(video);
      destroy = () => hls.destroy();
    });

    return () => {
      cancelled = true;
      destroy?.();
    };
  }, [videoId]);

  return (
    <div className="rs-activity-player">
      <video
        ref={videoRef}
        className="rs-activity-video"
        controls
        playsInline
        aria-label={title}
      />
      {failed ? (
        <p className="rs-activity-player-error" role="alert">
          This clip couldn't be played here.
        </p>
      ) : null}
    </div>
  );
}
