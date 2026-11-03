import { useReducer } from "react";

/**
 * Clips chosen in the library list, to bin together. `allMatching` stands for every clip the list's filters match,
 * loaded or not, so binning goes by the filter rather than by the rows on screen.
 */
export type ClipSelection = {
  /** Choosing is under way: rows toggle instead of opening, even with nothing chosen yet. */
  active: boolean;
  selected: ReadonlySet<string>;
  allMatching: boolean;
  /** The last clip toggled one at a time, where a shift-click's run starts. */
  anchor: string | null;
};

export type ClipSelectionAction =
  | { type: "start" }
  | { type: "clear" }
  /** One clip, or with `range` the run from the anchor to it in `order`, set to the anchor's new state. */
  | { type: "toggle"; clipId: string; range: boolean; order: readonly string[] }
  | {
      type: "set";
      clipIds: readonly string[];
      selected: boolean;
      order: readonly string[];
    }
  | { type: "allMatching" };

export const emptySelection: ClipSelection = {
  active: false,
  selected: new Set(),
  allMatching: false,
  anchor: null,
};

/** The chosen clips spelled out, for when "every match" gives way to choosing one at a time. */
function explicit(state: ClipSelection, order: readonly string[]) {
  return new Set(state.allMatching ? order : state.selected);
}

export function clipSelectionReducer(
  state: ClipSelection,
  action: ClipSelectionAction,
): ClipSelection {
  switch (action.type) {
    case "start":
      return { ...state, active: true };
    case "clear":
      return emptySelection;
    case "allMatching":
      return { ...state, active: true, allMatching: true };
    case "set": {
      const selected = explicit(state, action.order);
      for (const clipId of action.clipIds) {
        if (action.selected) selected.add(clipId);
        else selected.delete(clipId);
      }
      return { ...state, active: true, allMatching: false, selected };
    }
    case "toggle": {
      const selected = explicit(state, action.order);
      const anchorAt = state.anchor ? action.order.indexOf(state.anchor) : -1;
      const at = action.order.indexOf(action.clipId);
      if (action.range && anchorAt !== -1 && at !== -1) {
        const value = selected.has(state.anchor!);
        const [from, to] = anchorAt < at ? [anchorAt, at] : [at, anchorAt];
        for (const clipId of action.order.slice(from, to + 1)) {
          if (value) selected.add(clipId);
          else selected.delete(clipId);
        }
        return { ...state, active: true, allMatching: false, selected };
      }
      if (selected.has(action.clipId)) selected.delete(action.clipId);
      else selected.add(action.clipId);
      return {
        active: true,
        allMatching: false,
        selected,
        anchor: action.clipId,
      };
    }
  }
}

export function isChosen(selection: ClipSelection, clipId: string) {
  return selection.allMatching || selection.selected.has(clipId);
}

/** How many clips are chosen, counting every match when the whole filter is. */
export function chosenCount(selection: ClipSelection, matching: number) {
  return selection.allMatching ? matching : selection.selected.size;
}

export function useClipSelection() {
  return useReducer(clipSelectionReducer, emptySelection);
}
