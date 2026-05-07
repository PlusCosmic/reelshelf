import {
  formatBytes,
  statusMessage,
  useClipUpload,
} from "@/hooks/useClipUpload";

type ClipUploadState = ReturnType<typeof useClipUpload>;

export function UploadProgress({ upload }: { upload: ClipUploadState }) {
  if (upload.status === "ready") return null;

  return (
    <div className="rs-upload-progress">
      <div className="rs-upload-progress-track">
        <span style={{ width: `${upload.progress}%` }} />
      </div>
      <div className="rs-upload-progress-meta">
        <span>{statusMessage(upload.status)}</span>
        <span>{upload.progress}%</span>
      </div>
      {upload.status === "uploading" || upload.status === "paused" ? (
        <div className="rs-meta">
          {formatBytes(upload.bytesUploaded)} of{" "}
          {formatBytes(upload.file!.size)}
          {upload.speed > 0 ? ` - ${formatBytes(upload.speed)}/s` : ""}
        </div>
      ) : null}
    </div>
  );
}
