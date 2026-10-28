import { describe, expect, it } from "vitest";
import {
  leanSupport,
  leftShelves,
  rightShelves,
  type SideShelfBook,
} from "./landingSideShelves";

const up = (height: number): SideShelfBook => ({
  kind: "upright",
  width: 30,
  height,
  cloth: "#000000",
});

const lean = (toward: "left" | "right", height = 200): SideShelfBook => ({
  kind: "leaning",
  toward,
  width: 30,
  height,
  cloth: "#000000",
});

describe("landing side shelves", () => {
  it("rests every leaning book against an upright it can reach", () => {
    for (const row of [...leftShelves, ...rightShelves]) {
      row.forEach((book, index) => {
        if (book.kind === "leaning")
          expect(leanSupport(row, index)).not.toBeNull();
      });
    }
  });

  it("leans every book toward the window edge, away from the cropped end beside the content", () => {
    const leans = (shelves: SideShelfBook[][]) =>
      shelves
        .flat()
        .flatMap((book) => (book.kind === "leaning" ? [book.toward] : []));
    expect(leans(leftShelves)).not.toContain("right");
    expect(leans(rightShelves)).not.toContain("left");
  });

  it("finds no support when a book leans into empty space", () => {
    expect(leanSupport([up(220), lean("right")], 1)).toBeNull();
    expect(leanSupport([lean("left"), up(220)], 0)).toBeNull();
  });

  it("finds no support against a stack or a shorter book", () => {
    const flat: SideShelfBook = { kind: "stack", books: [] };
    expect(leanSupport([flat, lean("left")], 1)).toBeNull();
    expect(leanSupport([up(150), lean("left", 220)], 1)).toBeNull();
  });

  it("finds the upright on the side the book leans to", () => {
    const support = up(220);
    expect(leanSupport([support, lean("left")], 1)).toBe(support);
  });
});
