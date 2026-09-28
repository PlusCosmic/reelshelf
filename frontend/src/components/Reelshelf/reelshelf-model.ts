import type {
  Clip,
  ClipCategoryTotals,
  GameCategoryResponse,
} from "@/api-client";
import { apiConfig } from "@/shared/config/apiConfig";

export interface GameShelfItem extends GameCategoryResponse {
  clipCount: number;
  unviewedCount: number;
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

const shortDateFormatter = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

export function getGameColors(id: string): [string, string] {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return palette[hash % palette.length];
}

/**
 * Shelf tiles use the API's per-category totals rather than counting the clips in the payload,
 * which stops at a preview page per category and so undercounts a full shelf.
 */
export function makeGameShelf(
  categories: GameCategoryResponse[] = [],
  categoryTotals: ClipCategoryTotals[] = [],
): GameShelfItem[] {
  const totalsByCategory = new Map(
    categoryTotals.map((totals) => [totals.gameCategoryId, totals]),
  );

  return categories.map((category) => {
    const totals = totalsByCategory.get(category.id);
    const [colorA, colorB] = getGameColors(category.id);
    return {
      ...category,
      clipCount: totals?.clipCount ?? 0,
      unviewedCount: totals?.unviewedCount ?? 0,
      durationSeconds: totals?.durationSeconds ?? 0,
      sizeBytes: totals?.storageBytes ?? 0,
      colorA,
      colorB,
    };
  });
}

export interface BookBinding {
  /** Cloth colour: the cover-derived one from the API, else the game's fallback colour. */
  cloth: string;
  /** Title ink that reads on the cloth. */
  ink: string;
  /** True for pale cloth, which takes dark ink and a fainter cover weave. */
  light: boolean;
}

const darkInk = "#13261c";
const lightInk = "#f4f7f4";

function relativeLuminance(hex: string) {
  const channel = (offset: number) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

function contrast(a: number, b: number) {
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/**
 * How a game's book is bound. Custom categories and games whose cover has not been read yet have
 * no cloth colour; they fall back to the darker of their placeholder pair, which takes light ink.
 */
export function bookBinding(game: GameShelfItem): BookBinding {
  const cloth = game.clothColor;
  if (!cloth || !/^#[0-9a-f]{6}$/i.test(cloth)) {
    return { cloth: game.colorB, ink: lightInk, light: false };
  }
  const luminance = relativeLuminance(cloth);
  const light =
    contrast(luminance, relativeLuminance(darkInk)) >
    contrast(luminance, relativeLuminance(lightInk));
  return { cloth, ink: light ? darkInk : lightInk, light };
}

function hashOf(text: string) {
  let hash = 0;
  for (const char of text) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return hash;
}

/** Spine size in desktop pixels: busier games are thicker, heights vary like a real shelf. */
export function bookSize(game: GameShelfItem) {
  const fullness = Math.sqrt(Math.min(game.clipCount, 300) / 300);
  return {
    width: Math.round(46 + 44 * fullness),
    height: 236 + (hashOf(game.id) % 65),
  };
}

/**
 * Splits a series prefix onto its own line of the spine, so "Call of Duty: Warzone" reads as a
 * small "Call of Duty" over a large "Warzone" instead of one long line that has to be cut.
 */
export function spineTitle(name: string): {
  series: string | null;
  title: string;
} {
  const split = name.indexOf(": ");
  if (split > 0 && name.length > 18) {
    return { series: name.slice(0, split), title: name.slice(split + 2) };
  }
  return { series: null, title: name };
}

export function categoryTotalsFor(
  categoryTotals: ClipCategoryTotals[],
  categoryId: string,
) {
  const totals = categoryTotals.find(
    (item) => item.gameCategoryId === categoryId,
  );
  return {
    clipCount: totals?.clipCount ?? 0,
    unviewedCount: totals?.unviewedCount ?? 0,
    durationSeconds: totals?.durationSeconds ?? 0,
    storageBytes: totals?.storageBytes ?? 0,
  };
}

export function categoryForClip(
  clip: Clip,
  categories: GameCategoryResponse[],
) {
  return categories.find(
    (category) =>
      category.id === clip.gameCategoryId ||
      category.slug === clip.categorySlug,
  );
}

export function thumbnailUrl(clip: Clip) {
  return videoThumbnailUrl(clip.video.guid);
}

/** A clip's thumbnail from its video id alone, which is its Bunny video guid. */
export function videoThumbnailUrl(videoId: string) {
  return `${apiConfig.bunnyBaseUrl}/${videoId}/thumbnail.jpg`;
}

export function playerUrl(clip: Clip) {
  return `https://player.mediadelivery.net/embed/${clip.video.videoLibraryId}/${clip.video.guid}?autoplay=false`;
}

export function formatDuration(seconds = 0) {
  const safeSeconds = Math.max(0, Math.floor(seconds));
  const h = Math.floor(safeSeconds / 3600);
  const m = Math.floor((safeSeconds % 3600) / 60);
  const s = safeSeconds % 60;
  if (h > 0)
    return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

export function formatDate(date: Date | string | undefined) {
  if (!date) return "Unknown";
  return shortDateFormatter.format(new Date(date));
}

// Non-breaking spaces keep the number and unit on one line when a stats line wraps.
export function formatSize(bytes = 0) {
  if (!bytes) return "0\u00a0MB";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value >= 10 ? value.toFixed(0) : value.toFixed(1)}\u00a0${units[unit]}`;
}
