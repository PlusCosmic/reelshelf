// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MoveClipsDialog } from "./MoveClipsDialog";
import type { GameShelfItem } from "./reelshelf-model";

afterEach(() => cleanup());

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

function renderDialog(
  books: GameShelfItem[],
  count: number | "all" = 3,
  pending = false,
) {
  const handlers = {
    onMove: vi.fn(),
    onAnotherGame: vi.fn(),
    onClose: vi.fn(),
  };
  render(
    <MoveClipsDialog
      count={count}
      books={books}
      pending={pending}
      {...handlers}
    />,
  );
  return handlers;
}

describe("MoveClipsDialog", () => {
  it("lists the books the clips can go to and moves them to the one chosen", () => {
    const doom = book("doom", "Doom", 1);
    const { onMove } = renderDialog([book("apex", "Apex Legends", 12), doom]);

    expect(
      screen.getByRole("dialog", {
        name: "Which book do these 3 clips belong in?",
      }),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Doom, 1 clip" }));
    expect(onMove).toHaveBeenCalledWith(doom);
  });

  it("offers another game, even with no other book on the shelf", () => {
    const { onAnotherGame } = renderDialog([], "all");

    expect(
      screen.getByText("There are no other books on your shelf yet."),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Another game…" }));
    expect(onAnotherGame).toHaveBeenCalled();
  });

  it("holds every choice while a move is under way", () => {
    renderDialog([book("doom", "Doom", 1)], 1, true);

    expect(
      (
        screen.getByRole("button", {
          name: "Doom, 1 clip",
        }) as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    expect(screen.getByRole("status").textContent).toBe("Moving…");
  });
});
