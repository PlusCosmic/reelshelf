import { describe, expect, it } from "vitest";
import {
  clipListSearchFor,
  librarySearchOf,
  listFiltersFor,
  readClipListSearch,
  readLibrarySearch,
} from "./libraryList";

describe("libraryList", () => {
  it("reads the filters from a URL and drops empty or unknown ones", () => {
    expect(
      readLibrarySearch({ q: "clutch", tag: "", legend: 3, page: "2" }),
    ).toEqual({ q: "clutch" });
    expect(readLibrarySearch({})).toEqual({});
  });

  it("carries the list a clip was opened from, and gives the filters back", () => {
    const inBook = clipListSearchFor({ tag: "whiff" }, true);
    expect(inBook).toEqual({ tag: "whiff" });
    const everyGame = clipListSearchFor({ q: "ace" }, false);
    expect(everyGame).toEqual({ q: "ace", from: "library" });
    expect(librarySearchOf({ ...everyGame, sort: true })).toEqual({ q: "ace" });
  });

  it("reads a clip page's list context", () => {
    expect(
      readClipListSearch({ legend: "Wraith", from: "library", sort: "true" }),
    ).toEqual({ legend: "Wraith", from: "library", sort: true });
    expect(readClipListSearch({ from: "elsewhere", sort: "no" })).toEqual({});
  });

  it("steps through the clip's book, or every game when it came from the library", () => {
    expect(listFiltersFor({ tag: "ace" }, "apex")).toEqual({
      categoryId: "apex",
      search: undefined,
      tag: "ace",
      legend: null,
    });
    expect(listFiltersFor({ from: "library" }, "apex").categoryId).toBeNull();
  });
});
