import { createFileRoute, useNavigate } from "@tanstack/react-router";
import {
  IconCheck,
  IconPlayerPause,
  IconPlayerPlay,
  IconRotateClockwise,
  IconUpload,
  IconX,
} from "@tabler/icons-react";
import { useRef, useState } from "react";
import { useCategories } from "@/hooks/queries";
import {
  formatBytes,
  statusMessage,
  useClipUpload,
} from "@/hooks/useClipUpload";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

function UploadRoute() {
  const navigate = useNavigate();
  const { data: categories = [], isLoading: categoriesLoading } =
    useCategories();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [categoryId, setCategoryId] = useState("");
  const selectedCategoryId = categoryId || categories[0]?.id || "";
  const upload = useClipUpload(selectedCategoryId);
  const selectedCategory = categories.find(
    (category) => category.id === selectedCategoryId,
  );

  return (
    <section className="rs-upload-page">
      <div className="rs-eyebrow">Step {upload.step} of 3 - Upload</div>
      <h1 className="rs-display rs-upload-title">
        {upload.step === 1 ? (
          <>
            Drop a clip <em>onto the shelf</em>.
          </>
        ) : upload.step === 2 ? (
          <>Sending the clip...</>
        ) : upload.status === "done" ? (
          <>
            Uploaded. <em>Ready for the shelf.</em>
          </>
        ) : (
          <>
            Almost there. <em>Check the details.</em>
          </>
        )}
      </h1>

      <div className="rs-upload-steps" aria-label={`Step ${upload.step} of 3`}>
        {[1, 2, 3].map((item) => (
          <span
            key={item}
            className={item <= upload.step ? "active" : undefined}
          />
        ))}
      </div>

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

      {!upload.file && upload.error ? (
        <div className="rs-upload-error">{upload.error}</div>
      ) : null}

      {upload.file ? (
        <div className="rs-upload-panel">
          <div className="rs-upload-file">
            <div className="rs-upload-thumb" />
            <div>
              <div className="rs-upload-file-name">{upload.file.name}</div>
              <div className="rs-meta">
                {upload.fileMeta?.size} -{" "}
                {selectedCategory?.name ?? "Choose a game"}
              </div>
            </div>
          </div>

          <div className="rs-upload-form-grid">
            <div className="rs-field">
              <label htmlFor="clip-title">Title</label>
              <input
                id="clip-title"
                value={upload.title}
                onChange={(event) => upload.setTitle(event.currentTarget.value)}
                disabled={upload.isDetailsLocked}
              />
            </div>
            <div className="rs-field">
              <label htmlFor="clip-category">Game</label>
              <select
                id="clip-category"
                value={selectedCategoryId}
                onChange={(event) => setCategoryId(event.currentTarget.value)}
                disabled={categoriesLoading || upload.isDetailsLocked}
              >
                <option value="">Choose a game</option>
                {categories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="rs-field">
              <label htmlFor="clip-created">Recorded at</label>
              <input
                id="clip-created"
                type="datetime-local"
                value={upload.createdAt}
                onChange={(event) =>
                  upload.setCreatedAt(event.currentTarget.value)
                }
                disabled={upload.isDetailsLocked}
              />
            </div>
          </div>

          {upload.status !== "ready" ? (
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
                  {formatBytes(upload.file.size)}
                  {upload.speed > 0 ? ` - ${formatBytes(upload.speed)}/s` : ""}
                </div>
              ) : null}
            </div>
          ) : null}

          {upload.error ? (
            <div className="rs-upload-error">{upload.error}</div>
          ) : null}

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
              <button
                className="rs-primary"
                type="button"
                onClick={() => void navigate({ to: "/" })}
              >
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
        </div>
      ) : null}
    </section>
  );
}
