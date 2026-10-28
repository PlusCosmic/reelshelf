import type {
  Clip,
  ClipCategoryTotals,
  GameCategoryResponse,
} from "@/api-client";
import { libraryCopy } from "@/copy/library";

export interface GameShelfItem extends GameCategoryResponse {
  clipCount: number;
  unviewedCount: number;
  durationSeconds: number;
  sizeBytes: number;
}

/**
 * Cloths for books with no colour from a cover: custom categories, and games whose cover hasn't been
 * read yet. Muted and mid-toned like the cloths the API takes from covers.
 */
const defaultCloths = [
  "#6b4f3a",
  "#3f5a4a",
  "#4a4f6b",
  "#6b3f45",
  "#5c5a3a",
  "#3f5660",
  "#5e4a66",
  "#7a6a4f",
];

const shortDateFormatter = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

/** The same book always gets the same default cloth. */
export function defaultCloth(id: string) {
  return defaultCloths[hashOf(id) % defaultCloths.length];
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
    return {
      ...category,
      clipCount: totals?.clipCount ?? 0,
      unviewedCount: totals?.unviewedCount ?? 0,
      durationSeconds: totals?.durationSeconds ?? 0,
      sizeBytes: totals?.storageBytes ?? 0,
    };
  });
}

export interface BookBinding {
  /** Cloth colour: the cover-derived one from the API, else a default cloth for the book. */
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
 * no cloth colour, and take a default cloth instead.
 */
export function bookBinding(game: GameCategoryResponse): BookBinding {
  const cloth =
    game.clothColor && /^#[0-9a-f]{6}$/i.test(game.clothColor)
      ? game.clothColor
      : defaultCloth(game.id);
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

/** The clip's thumbnail, or null until it exists (an R2 clip's worker hasn't made it yet). */
export function thumbnailUrl(clip: Clip) {
  return clip.video.thumbnailUrl;
}

/** An Apex legend's portrait in `public/apex-legends/`, named after the legend ("Mad Maggie" is `mad-maggie.png`). */
export function legendPortraitUrl(legend: string) {
  const slug = legend
    .toLowerCase()
    .replace(/[^a-z]+/g, "-")
    .replace(/^-|-$/g, "");
  return `/apex-legends/${slug}.png`;
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
  if (!date) return libraryCopy.clip.unknownDate;
  return shortDateFormatter.format(new Date(date));
}
