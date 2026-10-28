/**
 * The decorative bookcases either side of the landing page's hero. Rows are written in screen order,
 * left to right, and the books are drawn as written, so nothing here is random.
 */

/** An upright book, standing on the shelf. */
type Upright = {
  kind: "upright";
  width: number;
  height: number;
  cloth: string;
};

/** A book tipped over to rest its top against the upright beside it, on the side it leans to. */
type Leaning = {
  kind: "leaning";
  toward: "left" | "right";
  width: number;
  height: number;
  cloth: string;
};

/** Books lying flat on top of each other, bottom one first. */
type Stack = {
  kind: "stack";
  books: Array<{ width: number; thickness: number; cloth: string }>;
};

export type SideShelfBook = Upright | Leaning | Stack;

export const LEAN_DEGREES = 9;

const cloths = {
  rust: "#b06c51",
  olive: "#928111",
  blue: "#326295",
  ink: "#2f2e57",
  grass: "#5a9148",
  plum: "#382541",
  brick: "#7a3b3b",
  teal: "#2e5d5a",
  tan: "#8a6a3a",
  slate: "#4b4f7a",
  wine: "#6d2f4f",
  moss: "#3f6b3a",
};

const up = (width: number, height: number, cloth: string): Upright => ({
  kind: "upright",
  width,
  height,
  cloth,
});

const lean = (
  toward: Leaning["toward"],
  width: number,
  height: number,
  cloth: string,
): Leaning => ({ kind: "leaning", toward, width, height, cloth });

const stack = (...books: Array<[number, number, string]>): Stack => ({
  kind: "stack",
  books: books.map(([width, thickness, cloth]) => ({
    width,
    thickness,
    cloth,
  })),
});

/**
 * The left bookcase's shelves, top first, packed against the window's left edge. The end beside the
 * content fades out and is cropped where the margin is narrow, so a book leans toward the window edge
 * and the upright holding it up always shows.
 */
export const leftShelves: SideShelfBook[][] = [
  [
    up(44, 222, cloths.ink),
    lean("left", 32, 196, cloths.rust),
    up(26, 188, cloths.grass),
    up(38, 214, cloths.blue),
    up(30, 234, cloths.brick),
    up(40, 196, cloths.slate),
    up(28, 226, cloths.teal),
    up(34, 208, cloths.tan),
  ],
  [
    up(28, 192, cloths.moss),
    up(34, 226, cloths.slate),
    up(42, 204, cloths.tan),
    up(26, 232, cloths.brick),
    up(30, 218, cloths.wine),
    stack(
      [150, 24, cloths.teal],
      [136, 20, cloths.olive],
      [144, 26, cloths.ink],
    ),
  ],
  [
    up(26, 228, cloths.blue),
    up(32, 198, cloths.grass),
    up(38, 220, cloths.plum),
    lean("left", 28, 200, cloths.olive),
    up(40, 236, cloths.teal),
    up(30, 206, cloths.slate),
    up(36, 230, cloths.rust),
  ],
];

/** The right bookcase's shelves, top first, the mirror of the left: packed against the right edge. */
export const rightShelves: SideShelfBook[][] = [
  [
    stack([140, 22, cloths.rust], [128, 26, cloths.tan]),
    up(34, 220, cloths.ink),
    up(28, 198, cloths.brick),
    up(42, 236, cloths.grass),
    up(30, 210, cloths.plum),
    up(36, 228, cloths.wine),
  ],
  [
    up(26, 190, cloths.teal),
    up(44, 224, cloths.ink),
    up(32, 200, cloths.rust),
    up(36, 232, cloths.olive),
    up(28, 214, cloths.moss),
    lean("right", 30, 194, cloths.tan),
    up(40, 226, cloths.slate),
  ],
  [
    stack([146, 24, cloths.plum], [132, 20, cloths.rust]),
    up(38, 230, cloths.brick),
    lean("right", 32, 198, cloths.blue),
    up(30, 226, cloths.grass),
    up(40, 206, cloths.wine),
    up(28, 234, cloths.ink),
    up(34, 216, cloths.olive),
  ],
];

/**
 * Where a leaning book stands: it pivots on its bottom corner on the side it leans to, so it stands
 * off its neighbour by as much as its top tips over, and touches it at the height its top drops to.
 */
export function leanGeometry(height: number) {
  const radians = (LEAN_DEGREES * Math.PI) / 180;
  return {
    offset: Math.round(height * Math.sin(radians)),
    contactHeight: height * Math.cos(radians),
  };
}

/** The upright a leaning book rests against, or null when nothing would hold it up. */
export function leanSupport(row: SideShelfBook[], index: number) {
  const book = row[index];
  if (book?.kind !== "leaning") return null;
  const neighbour = row[book.toward === "left" ? index - 1 : index + 1];
  if (neighbour?.kind !== "upright") return null;
  return leanGeometry(book.height).contactHeight <= neighbour.height
    ? neighbour
    : null;
}
