import { createFileRoute, useNavigate } from "@tanstack/react-router";
import {
  IconCheck,
  IconPlayerPause,
  IconPlayerPlay,
  IconRotateClockwise,
  IconUpload,
  IconX,
} from "@tabler/icons-react";
import { ApiError } from "@/shared/services/api-error";
import type { ChangeEvent, DragEvent } from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import * as tus from "tus-js-client";
import { useCategories, useCreateVideo } from "@/hooks/queries";
import { calculateFileMD5 } from "@/utils/fileHash";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

type UploadStatus =
  | "idle"
  | "ready"
  | "hashing"
  | "creating"
  | "uploading"
  | "paused"
  | "done"
  | "error";

const MAX_FILE_SIZE = 4 * 1024 ** 3;
const VIDEO_TYPES = new Set([
  "video/mp4",
  "video/quicktime",
  "video/webm",
  "video/x-matroska",
]);

function UploadRoute() {
  const navigate = useNavigate();
  const { data: categories = [], isLoading: categoriesLoading } =
    useCategories();
  const createVideo = useCreateVideo();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const uploadRef = useRef<tus.Upload | null>(null);
  const [status, setStatus] = useState<UploadStatus>("idle");
  const [dragOver, setDragOver] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [createdAt, setCreatedAt] = useState("");
  const [progress, setProgress] = useState(0);
  const [bytesUploaded, setBytesUploaded] = useState(0);
  const [speed, setSpeed] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const startedAtRef = useRef<number | null>(null);

  const selectedCategoryId = categoryId || categories[0]?.id || "";
  const selectedCategory = categories.find(
    (category) => category.id === selectedCategoryId,
  );
  const step =
    status === "idle" || status === "ready" || (status === "error" && !file)
      ? 1
      : status === "done" || status === "error"
        ? 3
        : 2;
  const canUpload =
    Boolean(file && selectedCategoryId && title.trim()) &&
    (status === "ready" || status === "error");

  const fileMeta = useMemo(() => {
    if (!file) return null;
    return { size: formatBytes(file.size) };
  }, [file]);

  useEffect(() => {
    return () => {
      uploadRef.current?.abort();
    };
  }, []);

  function chooseFile(nextFile: File) {
    if (!isVideoFile(nextFile)) {
      setError("Choose an MP4, MOV, WebM, or MKV video file.");
      setStatus("idle");
      return;
    }
    if (nextFile.size > MAX_FILE_SIZE) {
      setError("This file is larger than the 4 GB upload limit.");
      setStatus("idle");
      return;
    }

    uploadRef.current?.abort();
    setFile(nextFile);
    setTitle(stripExtension(nextFile.name));
    setCreatedAt(toLocalDateTimeInput(new Date(nextFile.lastModified)));
    setProgress(0);
    setBytesUploaded(0);
    setSpeed(0);
    setError(null);
    setStatus("ready");
  }

  function onInputChange(event: ChangeEvent<HTMLInputElement>) {
    const nextFile = event.currentTarget.files?.[0];
    if (nextFile) chooseFile(nextFile);
    event.currentTarget.value = "";
  }

  function onDrop(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setDragOver(false);
    const nextFile = event.dataTransfer.files[0];
    if (nextFile) chooseFile(nextFile);
  }

  async function startUpload() {
    if (!file || !selectedCategoryId || !title.trim()) return;
    try {
      setError(null);
      setProgress(0);
      setBytesUploaded(0);
      setSpeed(0);
      setStatus("hashing");
      const md5Hash = await calculateFileMD5(file);

      setStatus("creating");
      const response = await createVideo.mutateAsync({
        categoryId: selectedCategoryId,
        title: title.trim(),
        md5Hash,
        createdAt: createdAt
          ? new Date(createdAt)
          : new Date(file.lastModified),
      });

      if (!response?.signature) {
        throw new Error("The upload could not be prepared.");
      }

      startedAtRef.current = Date.now();
      setStatus("uploading");

      const upload = new tus.Upload(file, {
        endpoint: "https://video.bunnycdn.com/tusupload",
        retryDelays: [0, 1000, 3000, 5000, 10000],
        metadata: {
          filename: file.name,
          filetype: file.type || "video/mp4",
          title: title.trim(),
          collection: response.collectionId,
        },
        headers: {
          AuthorizationSignature: response.signature,
          AuthorizationExpire: response.expiration.toString(),
          VideoId: response.videoId,
          LibraryId: response.libraryId,
        },
        onProgress: (uploaded, total) => {
          const elapsedSeconds = startedAtRef.current
            ? (Date.now() - startedAtRef.current) / 1000
            : 0;
          setBytesUploaded(uploaded);
          setSpeed(elapsedSeconds > 0 ? uploaded / elapsedSeconds : 0);
          setProgress(Math.round((uploaded / total) * 100));
        },
        onSuccess: () => {
          setProgress(100);
          setBytesUploaded(file.size);
          setStatus("done");
        },
        onError: (uploadError) => {
          setError(uploadError.message);
          setStatus("error");
        },
      });

      uploadRef.current = upload;
      const previousUploads = await upload.findPreviousUploads();
      if (previousUploads.length)
        upload.resumeFromPreviousUpload(previousUploads[0]);
      upload.start();
    } catch (uploadError) {
      if (uploadError instanceof ApiError && uploadError.status === 409) {
        setError("This video has already been uploaded.");
      } else {
        setError(
          uploadError instanceof Error
            ? uploadError.message
            : "The upload failed.",
        );
      }
      setStatus("error");
    }
  }

  function pauseUpload() {
    uploadRef.current?.abort();
    setStatus("paused");
  }

  function resumeUpload() {
    if (!uploadRef.current) return;
    startedAtRef.current = Date.now();
    setStatus("uploading");
    uploadRef.current.start();
  }

  function resetUpload() {
    uploadRef.current?.abort(true);
    uploadRef.current = null;
    startedAtRef.current = null;
    setStatus("idle");
    setFile(null);
    setTitle("");
    setCreatedAt("");
    setProgress(0);
    setBytesUploaded(0);
    setSpeed(0);
    setError(null);
  }

  return (
    <section className="rs-upload-page">
      <div className="rs-eyebrow">Step {step} of 3 - Upload</div>
      <h1 className="rs-display rs-upload-title">
        {step === 1 ? (
          <>
            Drop a clip <em>onto the shelf</em>.
          </>
        ) : step === 2 ? (
          <>Sending the clip...</>
        ) : status === "done" ? (
          <>
            Uploaded. <em>Ready for the shelf.</em>
          </>
        ) : (
          <>
            Almost there. <em>Check the details.</em>
          </>
        )}
      </h1>

      <div className="rs-upload-steps" aria-label={`Step ${step} of 3`}>
        {[1, 2, 3].map((item) => (
          <span key={item} className={item <= step ? "active" : undefined} />
        ))}
      </div>

      <input
        ref={fileInputRef}
        className="rs-visually-hidden"
        type="file"
        accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
        onChange={onInputChange}
      />

      {step === 1 ? (
        <button
          className={`rs-upload-dropzone${dragOver ? " active" : ""}`}
          type="button"
          onClick={() => fileInputRef.current?.click()}
          onDragOver={(event) => {
            event.preventDefault();
            setDragOver(true);
          }}
          onDragLeave={() => setDragOver(false)}
          onDrop={onDrop}
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

      {!file && error ? <div className="rs-upload-error">{error}</div> : null}

      {file ? (
        <div className="rs-upload-panel">
          <div className="rs-upload-file">
            <div className="rs-upload-thumb" />
            <div>
              <div className="rs-upload-file-name">{file.name}</div>
              <div className="rs-meta">
                {fileMeta?.size} - {selectedCategory?.name ?? "Choose a game"}
              </div>
            </div>
          </div>

          <div className="rs-upload-form-grid">
            <div className="rs-field">
              <label htmlFor="clip-title">Title</label>
              <input
                id="clip-title"
                value={title}
                onChange={(event) => setTitle(event.currentTarget.value)}
                disabled={
                  status === "hashing" ||
                  status === "creating" ||
                  status === "uploading"
                }
              />
            </div>
            <div className="rs-field">
              <label htmlFor="clip-category">Game</label>
              <select
                id="clip-category"
                value={selectedCategoryId}
                onChange={(event) => setCategoryId(event.currentTarget.value)}
                disabled={
                  categoriesLoading ||
                  status === "hashing" ||
                  status === "creating" ||
                  status === "uploading"
                }
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
                value={createdAt}
                onChange={(event) => setCreatedAt(event.currentTarget.value)}
                disabled={
                  status === "hashing" ||
                  status === "creating" ||
                  status === "uploading"
                }
              />
            </div>
          </div>

          {status !== "ready" ? (
            <div className="rs-upload-progress">
              <div className="rs-upload-progress-track">
                <span style={{ width: `${progress}%` }} />
              </div>
              <div className="rs-upload-progress-meta">
                <span>{statusMessage(status)}</span>
                <span>{progress}%</span>
              </div>
              {status === "uploading" || status === "paused" ? (
                <div className="rs-meta">
                  {formatBytes(bytesUploaded)} of {formatBytes(file.size)}
                  {speed > 0 ? ` - ${formatBytes(speed)}/s` : ""}
                </div>
              ) : null}
            </div>
          ) : null}

          {error ? <div className="rs-upload-error">{error}</div> : null}

          <div className="rs-upload-actions">
            {status === "ready" || status === "error" ? (
              <button
                className="rs-primary"
                type="button"
                disabled={!canUpload || createVideo.isPending}
                onClick={startUpload}
              >
                <IconUpload size={15} />
                Upload clip
              </button>
            ) : null}
            {status === "uploading" ? (
              <button
                className="rs-secondary"
                type="button"
                onClick={pauseUpload}
              >
                <IconPlayerPause size={15} />
                Pause
              </button>
            ) : null}
            {status === "paused" ? (
              <button
                className="rs-primary"
                type="button"
                onClick={resumeUpload}
              >
                <IconPlayerPlay size={15} />
                Resume
              </button>
            ) : null}
            {status === "done" ? (
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
              onClick={resetUpload}
            >
              {status === "done" || status === "error" ? (
                <IconRotateClockwise size={15} />
              ) : (
                <IconX size={15} />
              )}
              {status === "done" || status === "error"
                ? "Start over"
                : "Cancel"}
            </button>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function isVideoFile(file: File) {
  return VIDEO_TYPES.has(file.type) || /\.(mp4|mov|webm|mkv)$/i.test(file.name);
}

function stripExtension(name: string) {
  return name.replace(/\.[^.]+$/, "");
}

function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 16);
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes.toFixed(0)} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

function statusMessage(status: UploadStatus) {
  switch (status) {
    case "hashing":
      return "Reading file fingerprint...";
    case "creating":
      return "Preparing upload...";
    case "uploading":
      return "Uploading to video storage...";
    case "paused":
      return "Upload paused";
    case "done":
      return "Upload complete";
    case "error":
      return "Needs attention";
    default:
      return "Ready";
  }
}
