// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { LegendPicker } from "./LegendPicker";

vi.mock("./ReelshelfPrimitives", () => ({
  LegendPlate: () => null,
}));

afterEach(() => cleanup());

const legends = [
  { legend: "Wraith", count: 84 },
  { legend: "Octane", count: 33 },
  { legend: "Mad Maggie", count: 1 },
];

function renderPicker(value: string | null = null) {
  const onChange = vi.fn();
  render(<LegendPicker legends={legends} value={value} onChange={onChange} />);
  return onChange;
}

function openPanel() {
  fireEvent.click(screen.getByRole("button", { expanded: false }));
  return screen.getByRole("dialog", { name: "Played as" });
}

describe("LegendPicker", () => {
  it("opens a grid of every legend, most played first, with its clips", () => {
    renderPicker();
    expect(screen.queryByRole("dialog")).toBeNull();

    const panel = openPanel();
    const tiles = panel.querySelectorAll(".rs-legend-tile");
    expect(Array.from(tiles, (tile) => tile.textContent)).toEqual([
      "Wraith84 clips",
      "Octane33 clips",
      "Mad Maggie1 clip",
    ]);
    expect(panel.textContent).toContain("3 legends, most played first");
  });

  it("picks a legend and closes", () => {
    const onChange = renderPicker();
    openPanel();

    fireEvent.click(screen.getByRole("button", { name: /^Octane/ }));

    expect(onChange).toHaveBeenCalledWith("Octane");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("shows the legend picked, starts on it, and clears it", () => {
    const onChange = renderPicker("Octane");
    const trigger = screen.getByRole("button", {
      name: "Played as Octane. Change legend",
    });

    fireEvent.click(trigger);
    expect(document.activeElement?.textContent).toContain("Octane");
    fireEvent.click(screen.getByRole("button", { name: "Any legend" }));
    expect(onChange).toHaveBeenLastCalledWith(null);

    fireEvent.click(screen.getByRole("button", { name: "Show every legend" }));
    expect(onChange).toHaveBeenCalledTimes(2);
  });

  it("closes on Escape and hands focus back to its button", () => {
    renderPicker();
    openPanel();

    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(
      screen.getByRole("button", { name: "Filter by legend" }),
    );
  });

  it("closes when the page outside it is pressed", () => {
    renderPicker();
    openPanel();

    fireEvent.pointerDown(document.body);

    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
