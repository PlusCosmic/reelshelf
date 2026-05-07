import type { Clip, GameCategoryResponse, PlaylistSummary } from "@repo/clips-api-client";
import { apiConfig } from "@repo/shared/api-config";

export interface GameShelfItem extends GameCategoryResponse {
  clipCount: number;
  durationSeconds: number;
  sizeBytes: number;
  colorA: string;
  colorB: string;
}

const palette: Array<[string, string]> = [
  ["oklch(0.56 0.16 24)", "oklch(0.31 0.08 350)"],
  ["oklch(0.58 0.13 82)", "oklch(0.34 0.07 52)"],
  ["oklch(0.53 0.13 202)", "oklch(0.32 0.08 235)"],
  ["oklch(0.55 0.13 142)", "oklch(0.30 0.07 168)"],
  ["oklch(0.54 0.14 288)", "oklch(0.32 0.09 312)"],
  ["oklch(0.56 0.13 8)", "oklch(0.31 0.07 28)"],
  ["oklch(0.55 0.11 245)", "oklch(0.28 0.07 260)"],
  ["oklch(0.59 0.12 116)", "oklch(0.32 0.07 92)"],
];

export function getGameColors(id: string): [string, string] {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return palette[hash % palette.length];
}

export function makeGameShelf(
  categories: GameCategoryResponse[] = [],
  clips: Clip[] = [],
): GameShelfItem[] {
  return categories.map((category) => {
    const categoryClips = clips.filter((clip) => clip.gameCategoryId === category.id);
    const [colorA, colorB] = getGameColors(category.id);
    return {
      ...category,
      clipCount: categoryClips.length,
      durationSeconds: categoryClips.reduce((sum, clip) => sum + (clip.video.length || 0), 0),
      sizeBytes: categoryClips.reduce((sum, clip) => sum + (clip.video.storageSize || 0), 0),
      colorA,
      colorB,
    };
  });
}

export function categoryForClip(clip: Clip, categories: GameCategoryResponse[]) {
  return categories.find((category) => category.id === clip.gameCategoryId || category.slug === clip.categorySlug);
}

export function thumbnailUrl(clip: Clip) {
  return `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`;
}

export function previewUrl(clip: Clip) {
  return `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/preview.webp`;
}

export function playerUrl(clip: Clip) {
  return `https://player.mediadelivery.net/embed/${clip.video.videoLibraryId}/${clip.video.guid}?autoplay=false`;
}

export function formatDuration(seconds = 0) {
  const safeSeconds = Math.max(0, Math.floor(seconds));
  const h = Math.floor(safeSeconds / 3600);
  const m = Math.floor((safeSeconds % 3600) / 60);
  const s = safeSeconds % 60;
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

export function formatDate(date: Date | string | undefined) {
  if (!date) return "Unknown";
  return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric", year: "numeric" }).format(new Date(date));
}

export function formatSize(bytes = 0) {
  if (!bytes) return "0 MB";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value >= 10 ? value.toFixed(0) : value.toFixed(1)} ${units[unit]}`;
}

export function newestClips(clips: Clip[]) {
  return [...clips].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
}

export function topTags(clips: Clip[], limit = 8) {
  const counts = new Map<string, number>();
  for (const clip of clips) {
    for (const tag of clip.tags) counts.set(tag, (counts.get(tag) ?? 0) + 1);
  }
  return [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, limit);
}

export function collectionCoverClips(playlist: PlaylistSummary, clips: Clip[]) {
  const seeded = [...clips].sort((a, b) => `${playlist.id}:${a.clipId}`.localeCompare(`${playlist.id}:${b.clipId}`));
  return seeded.slice(0, 4);
}
