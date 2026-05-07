import {
  IconCheck,
  IconPlayerPause,
  IconPlayerPlay,
  IconRotateClockwise,
  IconUpload,
  IconX,
} from "@tabler/icons-react";
import type { useClipUpload } from "@/hooks/useClipUpload";

type ClipUploadState = ReturnType<typeof useClipUpload>;

export function UploadActions({
  onDone,
  upload,
}: {
  onDone: () => void;
  upload: ClipUploadState;
}) {
  return (
    <div className="rs-upload-actions">
      {upload.status === "ready" || upload.status === "error" ? (
        <button
          className="rs-primary"
          type="button"
          disabled={!upload.canUpload || upload.isCreatingVideo}
          onClick={upload.startUpload}
        >
          <IconUpload size={15} />
          Upload clip
        </button>
      ) : null}
      {upload.status === "uploading" ? (
        <button
          className="rs-secondary"
          type="button"
          onClick={upload.pauseUpload}
        >
          <IconPlayerPause size={15} />
          Pause
        </button>
      ) : null}
      {upload.status === "paused" ? (
        <button
          className="rs-primary"
          type="button"
          onClick={upload.resumeUpload}
        >
          <IconPlayerPlay size={15} />
          Resume
        </button>
      ) : null}
      {upload.status === "done" ? (
        <button className="rs-primary" type="button" onClick={onDone}>
          <IconCheck size={15} />
          Back to library
        </button>
      ) : null}
      <button
        className="rs-secondary"
        type="button"
        onClick={upload.resetUpload}
      >
        {upload.status === "done" || upload.status === "error" ? (
          <IconRotateClockwise size={15} />
        ) : (
          <IconX size={15} />
        )}
        {upload.status === "done" || upload.status === "error"
          ? "Start over"
          : "Cancel"}
      </button>
    </div>
  );
}
