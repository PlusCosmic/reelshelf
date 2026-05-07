import type { RefObject } from "react";
import { IconUpload } from "@tabler/icons-react";
import type { useClipUpload } from "@/hooks/useClipUpload";

type ClipUploadState = ReturnType<typeof useClipUpload>;

export function UploadDropzone({
  fileInputRef,
  upload,
}: {
  fileInputRef: RefObject<HTMLInputElement | null>;
  upload: ClipUploadState;
}) {
  return (
    <>
      <input
        ref={fileInputRef}
        className="rs-visually-hidden"
        type="file"
        accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
        onChange={upload.onInputChange}
      />

      {upload.step === 1 ? (
        <button
          className={`rs-upload-dropzone${upload.dragOver ? " active" : ""}`}
          type="button"
          onClick={() => fileInputRef.current?.click()}
          onDragOver={(event) => {
            event.preventDefault();
            upload.setDragOver(true);
          }}
          onDragLeave={() => upload.setDragOver(false)}
          onDrop={upload.onDrop}
        >
          <span className="rs-upload-drop-icon">
            <IconUpload size={26} />
          </span>
          <span className="rs-upload-drop-title">
            Drop a video, or click to browse
          </span>
          <span className="rs-upload-drop-copy">
            MP4, MOV, WebM, or MKV up to 4 GB.
          </span>
        </button>
      ) : null}
    </>
  );
}
