import {
  ClipVideoStatus,
  type Clip,
  type TwitchClipSummary,
} from "@/api-client";

export type UploadStatus =
  | "queued"
  | "hashing"
  | "creating"
  | "uploading"
  | "paused"
  | "uploaded"
  | "duplicate"
  | "error";

/** One file on its way into a book. It lives in the browser until Bunny has the whole file. */
export type UploadItem = {
  id: string;
  file: File;
  title: string;
  categoryId: string;
  createdAt: Date;
  sessionDate: string;
  status: UploadStatus;
  progress: number;
  clipId: string | null;
  error: string | null;
};

const ACTIVE: UploadStatus[] = ["hashing", "creating", "uploading"];
const UNFINISHED: UploadStatus[] = ["queued", "paused", ...ACTIVE];

export function isActiveUpload(item: UploadItem) {
  return ACTIVE.includes(item.status);
}

export function isUnfinishedUpload(item: UploadItem) {
  return UNFINISHED.includes(item.status);
}

export type ClipStage =
  | { kind: "uploading"; progress: number; paused: boolean }
  | { kind: "processing"; progress: number | null }
  | { kind: "failed" }
  | { kind: "ready" };

/**
 * Where a clip is on its way to the shelf. An upload still running in this browser wins; after that
 * the clip's video status says whether it is still being processed, failed, or done.
 */
export function clipStage(
  status: ClipVideoStatus,
  encodeProgress: number,
  upload?: UploadItem | null,
): ClipStage {
  if (upload && isUnfinishedUpload(upload)) {
    return {
      kind: "uploading",
      progress: upload.progress,
      paused: upload.status === "paused",
    };
  }
  switch (status) {
    case ClipVideoStatus.Ready:
      return { kind: "ready" };
    case ClipVideoStatus.EncodeFailed:
    case ClipVideoStatus.UploadFailed:
      return { kind: "failed" };
    default:
      return {
        kind: "processing",
        progress: encodeProgress > 0 ? encodeProgress : null,
      };
  }
}

export function clipStageFor(clip: Clip, upload?: UploadItem | null) {
  return clipStage(clip.video.status, clip.video.encodeProgress, upload);
}

/** True while a clip in the list can still change on its own, so the list is worth refreshing. */
export function clipStillArriving(clip: Clip) {
  const stage = clipStageFor(clip);
  return stage.kind === "processing";
}

export function clipStageLabel(stage: ClipStage) {
  switch (stage.kind) {
    case "uploading":
      return stage.paused ? "Paused" : `Uploading ${stage.progress}%`;
    case "processing":
      return stage.progress === null
        ? "Processing"
        : `Processing ${stage.progress}%`;
    case "failed":
      return "Couldn’t be processed";
    case "ready":
      return "";
  }
}

export type TwitchGhost = {
  igdbId: number;
  gameName: string;
  clips: TwitchClipSummary[];
};

export type TwitchSuggestions = {
  /** Clips not yet imported, keyed by the book their game already has on the shelf. */
  byCategory: Map<string, TwitchClipSummary[]>;
  /** Games with clips to import and no book yet, most clips first. */
  ghosts: TwitchGhost[];
};

/**
 * Sorts a page of the owner's Twitch clips into the books they belong in. Twitch names each clip's
 * game with an IGDB id, which is what books are made from, so a clip either matches a book on the
 * shelf or points at one that could be added. Clips already imported, and games with no IGDB entry
 * (Just Chatting and the like), are left out.
 */
export function sortTwitchClips(
  clips: TwitchClipSummary[],
  dismissedIgdbIds: ReadonlySet<number> = new Set(),
): TwitchSuggestions {
  const byCategory = new Map<string, TwitchClipSummary[]>();
  const ghostsById = new Map<number, TwitchGhost>();

  for (const clip of clips) {
    if (clip.alreadyImported) continue;
    if (clip.suggestedCategoryId) {
      const list = byCategory.get(clip.suggestedCategoryId) ?? [];
      list.push(clip);
      byCategory.set(clip.suggestedCategoryId, list);
      continue;
    }
    if (clip.igdbId === null || dismissedIgdbIds.has(clip.igdbId)) continue;
    const ghost = ghostsById.get(clip.igdbId) ?? {
      igdbId: clip.igdbId,
      gameName: clip.gameName ?? "Unknown game",
      clips: [],
    };
    ghost.clips.push(clip);
    ghostsById.set(clip.igdbId, ghost);
  }

  const ghosts = [...ghostsById.values()].sort(
    (a, b) => b.clips.length - a.clips.length,
  );
  return { byCategory, ghosts };
}
