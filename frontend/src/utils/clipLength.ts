import {
  clipLengthToleranceSeconds,
  formatClipLength,
} from "@/shared/services/plans";

/**
 * Why a clip this long can't be added on a plan that keeps clips of up to `maxSeconds`, or null when it can. The API
 * checks the uploaded file again when the upload completes; this only spares uploading a file it would refuse.
 */
export function clipLengthProblem(
  durationSeconds: number | null,
  maxSeconds: number | null | undefined,
): string | null {
  if (
    maxSeconds == null ||
    durationSeconds === null ||
    durationSeconds <= maxSeconds + clipLengthToleranceSeconds
  ) {
    return null;
  }

  return `This clip is ${formatClipLength(Math.round(durationSeconds))} long, and your plan keeps clips of up to ${formatClipLength(maxSeconds)}. Trim it, or upgrade for longer clips.`;
}

/** The file's length as the browser reads it, or null when it can't (an unsupported codec, say). */
export function readVideoDuration(
  file: File,
  timeoutMs = 10_000,
): Promise<number | null> {
  return new Promise((resolve) => {
    const video = document.createElement("video");
    const url = URL.createObjectURL(file);
    let settled = false;
    const finish = (duration: number | null) => {
      if (settled) return;
      settled = true;
      window.clearTimeout(timer);
      video.removeAttribute("src");
      video.load();
      URL.revokeObjectURL(url);
      resolve(duration);
    };
    const timer = window.setTimeout(() => finish(null), timeoutMs);
    video.preload = "metadata";
    video.muted = true;
    video.onloadedmetadata = () =>
      finish(Number.isFinite(video.duration) ? video.duration : null);
    video.onerror = () => finish(null);
    video.src = url;
  });
}
