import { describe, expect, it } from "vitest";
import {
  chosenCount,
  clipSelectionReducer,
  emptySelection,
  isChosen,
  type ClipSelection,
  type ClipSelectionAction,
} from "./clipSelection";

const order = ["a", "b", "c", "d", "e"];
const run = (...actions: ClipSelectionAction[]): ClipSelection =>
  actions.reduce(clipSelectionReducer, emptySelection);
const toggle = (clipId: string, range = false): ClipSelectionAction => ({
  type: "toggle",
  clipId,
  range,
  order,
});

describe("clipSelectionReducer", () => {
  it("toggles one clip at a time and starts choosing", () => {
    const state = run(toggle("b"), toggle("d"), toggle("b"));
    expect([...state.selected]).toEqual(["d"]);
    expect(state.active).toBe(true);
    expect(state.anchor).toBe("b");
  });

  it("chooses a run with shift, either way from the anchor", () => {
    expect([...run(toggle("b"), toggle("d", true)).selected].sort()).toEqual([
      "b",
      "c",
      "d",
    ]);
    expect([...run(toggle("d"), toggle("a", true)).selected].sort()).toEqual([
      "a",
      "b",
      "c",
      "d",
    ]);
  });

  it("clears a run when the anchor was just cleared", () => {
    const state = run(
      { type: "set", clipIds: order, selected: true, order },
      toggle("b"),
      toggle("d", true),
    );
    expect([...state.selected].sort()).toEqual(["a", "e"]);
  });

  it("treats every match as chosen until one is taken out", () => {
    const all = run({ type: "allMatching" });
    expect(isChosen(all, "zzz")).toBe(true);
    expect(chosenCount(all, 240)).toBe(240);

    const lessOne = clipSelectionReducer(all, toggle("c"));
    expect(lessOne.allMatching).toBe(false);
    expect([...lessOne.selected]).toEqual(["a", "b", "d", "e"]);
  });

  it("chooses a whole month and clears", () => {
    const state = run({
      type: "set",
      clipIds: ["c", "d"],
      selected: true,
      order,
    });
    expect(chosenCount(state, 99)).toBe(2);
    expect(clipSelectionReducer(state, { type: "clear" })).toBe(emptySelection);
  });
});
