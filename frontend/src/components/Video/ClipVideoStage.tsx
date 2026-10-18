import type { ComponentProps } from "react";
import { ClipVideoStatus, type PlaybackSource } from "@/api-client";
import { ClipVideoPlayer } from "./ClipVideoPlayer";

export type ClipProcessingMessage = {
  title: string;
  detail: string;
  failed: boolean;
  /** The encode's percentage, when the host reports one. */
  progress: number | null;
};

/** What the player area says about a clip whose video isn't ready yet. */
export function clipProcessingMessage(
  status: ClipVideoStatus,
  encodeProgress: number,
): ClipProcessingMessage {
  switch (status) {
    case ClipVideoStatus.EncodeFailed:
      return {
        title: "This clip couldn’t be processed.",
        detail: "Its video couldn’t be encoded. Try uploading it again.",
        failed: true,
        progress: null,
      };
    case ClipVideoStatus.UploadFailed:
      return {
        title: "This clip’s upload didn’t finish.",
        detail: "Try uploading it again.",
        failed: true,
        progress: null,
      };
    default: {
      // Awaiting its upload and uploaded are processing too, as far as a viewer is concerned.
      const progress =
        encodeProgress > 0 ? Math.min(encodeProgress, 100) : null;
      return {
        title: progress === null ? "Processing" : `Processing ${progress}%`,
        detail: "It’ll play here as soon as it’s ready.",
        failed: false,
        progress,
      };
    }
  }
}

/**
 * Stands in for the player while a clip's video isn't ready: its thumbnail once there is one (the game's cloth
 * behind shows until then), what is happening, and the encode's progress where the host reports it. Fills the
 * player's box like the video would.
 */
export function ClipProcessingState({
  status,
  encodeProgress = 0,
  thumbnailUrl,
}: {
  status: ClipVideoStatus;
  encodeProgress?: number;
  thumbnailUrl?: string | null;
}) {
  const message = clipProcessingMessage(status, encodeProgress);
  return (
    <div
      className={`rs-video-processing${message.failed ? " failed" : ""}`}
      role={message.failed ? "alert" : "status"}
    >
      {thumbnailUrl ? (
        <img
          // Keyed by URL, so a thumbnail that arrives later gets a fresh element rather than a hidden one.
          key={thumbnailUrl}
          className="rs-video-processing-thumb"
          src={thumbnailUrl}
          alt=""
          onError={(event) => {
            event.currentTarget.style.display = "none";
          }}
        />
      ) : null}
      <div className="rs-video-processing-note">
        {message.failed ? null : (
          <span className="rs-spinner" aria-hidden="true" />
        )}
        <div>
          <strong>{message.title}</strong>
          <span>{message.detail}</span>
        </div>
      </div>
      {message.progress !== null ? (
        <span className="rs-thumb-progress processing" aria-hidden="true">
          <span style={{ width: `${message.progress}%` }} />
        </span>
      ) : null}
    </div>
  );
}

/**
 * A clip's player area: the player once its video is ready, and its processing state until then, so nothing tries
 * to play a file that doesn't exist yet (the API hands out no sources before then either). "Can't play on this
 * device" is left to the player, for a ready clip this device can't decode.
 */
export function ClipVideoStage({
  status,
  encodeProgress,
  sources,
  poster,
  ...player
}: {
  status: ClipVideoStatus;
  encodeProgress?: number;
  sources: readonly PlaybackSource[];
  /** The thumbnail, or null until the clip has one. */
  poster?: string | null;
} & Omit<ComponentProps<typeof ClipVideoPlayer>, "sources" | "poster">) {
  if (status !== ClipVideoStatus.Ready) {
    return (
      <ClipProcessingState
        status={status}
        encodeProgress={encodeProgress}
        thumbnailUrl={poster}
      />
    );
  }
  return (
    <ClipVideoPlayer
      sources={sources}
      poster={poster ?? undefined}
      {...player}
    />
  );
}
