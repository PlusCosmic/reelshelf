import type { ChangeEvent, DragEvent } from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import * as tus from "tus-js-client";
import { useCreateVideo } from "@/hooks/clips.queries";
import {
  createTusClipUpload,
  prepareClipUpload,
  uploadErrorMessage,
} from "@/utils/clipUpload";

export type UploadStatus =
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

export function useClipUpload(selectedCategoryId: string) {
  const createVideo = useCreateVideo();
  const uploadRef = useRef<tus.Upload | null>(null);
  const startedAtRef = useRef<number | null>(null);
  const [status, setStatus] = useState<UploadStatus>("idle");
  const [dragOver, setDragOver] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [createdAt, setCreatedAt] = useState("");
  const [progress, setProgress] = useState(0);
  const [bytesUploaded, setBytesUploaded] = useState(0);
  const [speed, setSpeed] = useState(0);
  const [error, setError] = useState<string | null>(null);

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
  const isDetailsLocked =
    status === "hashing" || status === "creating" || status === "uploading";

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

      setStatus("creating");
      const { response } = await prepareClipUpload({
        categoryId: selectedCategoryId,
        createVideo: createVideo.mutateAsync,
        file,
        title: title.trim(),
        createdAt: createdAt
          ? new Date(createdAt)
          : new Date(file.lastModified),
      });

      startedAtRef.current = Date.now();
      setStatus("uploading");

      const upload = createTusClipUpload({
        file,
        response,
        title: title.trim(),
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
      if (previousUploads.length) {
        upload.resumeFromPreviousUpload(previousUploads[0]);
      }
      upload.start();
    } catch (uploadError) {
      setError(uploadErrorMessage(uploadError));
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

  return {
    bytesUploaded,
    canUpload,
    chooseFile,
    createdAt,
    dragOver,
    error,
    file,
    fileMeta,
    isCreatingVideo: createVideo.isPending,
    isDetailsLocked,
    onDrop,
    onInputChange,
    pauseUpload,
    progress,
    resetUpload,
    resumeUpload,
    setCreatedAt,
    setDragOver,
    setTitle,
    speed,
    startUpload,
    status,
    step,
    title,
  };
}

export function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes.toFixed(0)} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

export function statusMessage(status: UploadStatus) {
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
