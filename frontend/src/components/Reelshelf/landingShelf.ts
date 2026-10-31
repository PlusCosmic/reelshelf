import type { LandingShelfGame } from "@/api-client";
import { bookSize, type GameShelfItem } from "./reelshelf-model";

/** How many of the cached popular games one visit's example shelf shows. */
export const landingShelfSize = 12;

/**
 * The width the landing page's books may take, in desktop pixels at its 0.85 book scale: the case's 982 px row
 * less the pulled-out book (192 px), so a full shelf never needs scrolling on a desktop.
 */
export const landingShelfWidth = 982 - 192;
const landingBookScale = 0.85;
const bookGap = 3;

/** How long each book stays pulled out before the next one comes out. */
export const shelfCycleMs = 4500;

/**
 * One visit's example shelf: up to `count` of the popular games, picked and ordered at random, each with a made-up
 * clip count (so the spines vary in thickness the way a real library's do) and sometimes a few new clips. A book
 * that would take the shelf past `maxWidth` is passed over for a thinner one.
 */
export function shelfFromPopularGames(
  games: readonly LandingShelfGame[],
  count: number = landingShelfSize,
  random: () => number = Math.random,
  maxWidth: number = landingShelfWidth,
): GameShelfItem[] {
  const pool = [...games];
  for (let i = pool.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [pool[i], pool[j]] = [pool[j], pool[i]];
  }

  const seen = new Set<string>();
  const shelf: GameShelfItem[] = [];
  let width = 0;
  for (const game of pool) {
    const name = game.name.trim();
    const key = name.toLowerCase();
    if (!name || seen.has(key)) continue;

    // Most libraries hold a few dozen clips of a game and a few hold hundreds.
    const clipCount = Math.round(3 + random() ** 1.8 * 277);
    const unviewedCount =
      random() < 0.3 ? 1 + Math.floor(random() * Math.min(24, clipCount)) : 0;
    const book: GameShelfItem = {
      id: `igdb-${game.igdbId}`,
      name,
      slug: game.slug,
      coverUrl: game.coverUrl,
      keyArtUrl: null,
      gameLogoUrl: null,
      isCustom: false,
      clothColor: game.clothColor,
      clipCount,
      unviewedCount,
      durationSeconds: 0,
      sizeBytes: 0,
    };
    const bookWidth =
      Math.round(bookSize(book).width * landingBookScale) + bookGap;
    if (width + bookWidth > maxWidth) continue;
    seen.add(key);
    width += bookWidth;
    shelf.push(book);
    if (shelf.length === count) break;
  }
  return shelf;
}

/** The book that comes out after `current`, left to right and round again; the first when none is out. */
export function nextBookId(
  ids: readonly string[],
  current: string | null,
): string | null {
  if (ids.length === 0) return null;
  const index = current === null ? -1 : ids.indexOf(current);
  return ids[(index + 1) % ids.length];
}
