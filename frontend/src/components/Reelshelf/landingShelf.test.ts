import { describe, expect, it } from "vitest";
import type { LandingShelfGame } from "@/api-client";
import { nextBookId, shelfFromPopularGames } from "./landingShelf";
import { bookSize } from "./reelshelf-model";

const game = (igdbId: number, name: string): LandingShelfGame => ({
  igdbId,
  name,
  slug: name.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
  coverUrl: `https://images.igdb.com/igdb/image/upload/t_cover_big/co${igdbId}.jpg`,
  clothColor: igdbId % 2 ? "#b06c51" : null,
});

const popular = Array.from({ length: 36 }, (_, index) =>
  game(index + 1, `Game ${index + 1}`),
);

/** A repeatable stand-in for Math.random. */
function seeded(seed: number) {
  let state = seed;
  return () => {
    state = (state * 1103515245 + 12345) % 2147483648;
    return state / 2147483648;
  };
}

describe("shelfFromPopularGames", () => {
  it("shelves the asked-for number of different games", () => {
    const shelf = shelfFromPopularGames(popular, 12, seeded(1));

    expect(shelf).toHaveLength(12);
    expect(new Set(shelf.map((book) => book.id)).size).toBe(12);
  });

  it("picks a different shelf on a different visit", () => {
    const first = shelfFromPopularGames(popular, 12, seeded(1));
    const second = shelfFromPopularGames(popular, 12, seeded(2));

    expect(first.map((book) => book.id)).not.toEqual(
      second.map((book) => book.id),
    );
  });

  it("keeps each game's cover and cloth, and gives it some clips", () => {
    const [book] = shelfFromPopularGames([game(7, "Rust")], 12, seeded(3));

    expect(book).toMatchObject({
      id: "igdb-7",
      name: "Rust",
      coverUrl: "https://images.igdb.com/igdb/image/upload/t_cover_big/co7.jpg",
      clothColor: "#b06c51",
      isCustom: false,
    });
    expect(book.clipCount).toBeGreaterThanOrEqual(3);
    expect(book.unviewedCount).toBeLessThanOrEqual(book.clipCount);
  });

  it("shelves a game listed twice only once", () => {
    const shelf = shelfFromPopularGames(
      [game(1, "Rust"), game(2, "rust"), game(3, "Dota 2")],
      12,
      seeded(4),
    );

    expect(shelf.map((book) => book.name.toLowerCase()).sort()).toEqual([
      "dota 2",
      "rust",
    ]);
  });

  it("passes over books that would take the shelf past its width", () => {
    const shelf = shelfFromPopularGames(popular, 36, seeded(5), 400);
    const width = shelf.reduce(
      (total, book) => total + Math.round(bookSize(book).width * 0.85) + 3,
      0,
    );

    expect(shelf.length).toBeGreaterThan(0);
    expect(width).toBeLessThanOrEqual(400);
  });

  it("fits a dozen books in the desktop case nearly every visit", () => {
    const sizes = Array.from(
      { length: 200 },
      (_, seed) => shelfFromPopularGames(popular, 12, seeded(seed + 1)).length,
    );

    expect(sizes.filter((size) => size === 12).length).toBeGreaterThan(150);
    expect(Math.min(...sizes)).toBeGreaterThanOrEqual(10);
  });

  it("shelves nothing when there are no popular games yet", () => {
    expect(shelfFromPopularGames([], 12)).toEqual([]);
  });
});

describe("nextBookId", () => {
  const ids = ["a", "b", "c"];

  it("moves along the shelf and round again", () => {
    expect(nextBookId(ids, "a")).toBe("b");
    expect(nextBookId(ids, "c")).toBe("a");
  });

  it("starts at the first book when none is out", () => {
    expect(nextBookId(ids, null)).toBe("a");
    expect(nextBookId(ids, "gone")).toBe("a");
    expect(nextBookId([], null)).toBeNull();
  });
});
