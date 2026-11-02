// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { Bookcase } from "./Bookcase";
import type { GameShelfItem } from "./reelshelf-model";

afterEach(() => cleanup());

beforeAll(() => {
  window.matchMedia ??= ((query: string) => ({
    matches: query.includes("reduce"),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia;
  globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
  Element.prototype.scrollIntoView ??= () => {};
  Element.prototype.scrollTo ??= () => {};
});

function book(id: string, name: string, clipCount: number): GameShelfItem {
  return {
    id,
    name,
    slug: id,
    coverUrl: null,
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
    clothColor: null,
    clipCount,
    unviewedCount: 0,
    durationSeconds: 0,
    sizeBytes: 0,
  };
}

const shelf = [
  book("apex", "Apex Legends", 12),
  book("celeste", "Celeste", 0),
  book("doom", "Doom", 3),
];

function renderShelf(arranging: boolean) {
  const onReorder = vi.fn();
  const onRemoveBook = vi.fn();
  const onSelect = vi.fn();
  render(
    <Bookcase
      shelf={shelf}
      selectedId="doom"
      onSelect={onSelect}
      onPutBack={() => {}}
      onAddGame={() => {}}
      arranging={arranging}
      onReorder={onReorder}
      onRemoveBook={onRemoveBook}
    />,
  );
  return { onReorder, onRemoveBook, onSelect };
}

describe("Bookcase arranging", () => {
  it("leaves the shelf as it is until it is arranged", () => {
    renderShelf(false);
    expect(screen.queryByRole("button", { name: /off the shelf/ })).toBeNull();
    expect(screen.getByRole("button", { name: "Add a game or category" }));
    // The open book stands face-out.
    expect(screen.getByRole("button", { pressed: true }).textContent).toBe(
      "Doom",
    );
  });

  it("stands every book spine-out and offers to take off only the empty ones", () => {
    renderShelf(true);
    expect(screen.queryByRole("button", { pressed: true })).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Add a game or category" }),
    ).toBeNull();
    const removes = screen.getAllByRole("button", {
      name: /^Take .* off the shelf$/,
    });
    expect(removes.map((button) => button.getAttribute("aria-label"))).toEqual([
      "Take Celeste off the shelf",
    ]);
  });

  it("moves a book with the arrow keys and announces where it went", () => {
    const { onReorder, onSelect } = renderShelf(true);
    const apex = screen.getByRole("button", {
      name: /^Apex Legends, 12 clips\. Book 1 of 3/,
    });

    fireEvent.keyDown(apex, { key: "ArrowRight" });
    expect(onReorder).toHaveBeenLastCalledWith(["celeste", "apex", "doom"]);
    expect(screen.getByText("Apex Legends moved to 2 of 3")).toBeTruthy();

    fireEvent.keyDown(
      screen.getByRole("button", { name: /^Apex Legends.*Book 2 of 3/ }),
      { key: "End" },
    );
    expect(onReorder).toHaveBeenLastCalledWith(["celeste", "doom", "apex"]);

    fireEvent.click(screen.getByRole("button", { name: /^Doom/ }));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("takes an empty book off with its button or Delete, never a full one", () => {
    const { onRemoveBook } = renderShelf(true);

    fireEvent.click(
      screen.getByRole("button", { name: "Take Celeste off the shelf" }),
    );
    expect(onRemoveBook).toHaveBeenCalledWith(shelf[1]);

    fireEvent.keyDown(screen.getByRole("button", { name: /^Doom/ }), {
      key: "Delete",
    });
    expect(onRemoveBook).toHaveBeenCalledTimes(1);
  });
});
