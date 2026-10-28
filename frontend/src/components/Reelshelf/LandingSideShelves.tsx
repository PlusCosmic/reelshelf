import type { CSSProperties } from "react";
import {
  LEAN_DEGREES,
  leanGeometry,
  leftShelves,
  rightShelves,
  type SideShelfBook,
} from "./landingSideShelves";

// The gap between books on a side shelf, which a leaning book's offset already covers.
const BOOK_GAP = 2;

function Book({ book }: { book: SideShelfBook }) {
  if (book.kind === "stack") {
    return (
      <span className="rs-side-stack">
        {book.books.map((flat, index) => (
          <span
            key={index}
            style={
              {
                "--cloth": flat.cloth,
                width: flat.width,
                height: flat.thickness,
              } as CSSProperties
            }
          />
        ))}
      </span>
    );
  }

  const style = {
    "--cloth": book.cloth,
    width: book.width,
    height: book.height,
  } as CSSProperties;
  if (book.kind === "leaning") {
    const margin = leanGeometry(book.height).offset - BOOK_GAP;
    const left = book.toward === "left";
    Object.assign(style, {
      [left ? "marginLeft" : "marginRight"]: margin,
      transformOrigin: left ? "bottom left" : "bottom right",
      transform: `rotate(${left ? -LEAN_DEGREES : LEAN_DEGREES}deg)`,
    });
  }
  return <span className="rs-side-book" style={style} />;
}

function Bookcase({
  side,
  shelves,
}: {
  side: "left" | "right";
  shelves: SideShelfBook[][];
}) {
  return (
    <div className={`rs-landing-side ${side}`}>
      {shelves.map((row, rowIndex) => (
        <div className="rs-landing-side-row" key={rowIndex}>
          {row.map((book, index) => (
            <Book book={book} key={index} />
          ))}
        </div>
      ))}
    </div>
  );
}

/** Bookcases either side of the hero on wide screens, fading out before the example shelf. */
export function LandingSideShelves() {
  return (
    <div className="rs-landing-sides" aria-hidden="true">
      <Bookcase side="left" shelves={leftShelves} />
      <Bookcase side="right" shelves={rightShelves} />
    </div>
  );
}
