// @vitest-environment jsdom
import { describe, expect, it } from "vitest";
import type { ClipSearchInterpretation } from "@/api-client";
import {
  chipsFor,
  dateLabel,
  hasAnyFilter,
  loadClipSearch,
  saveClipSearch,
} from "./clipSearchModel";

const day = (iso: string) => new Date(iso);

const interpretation: ClipSearchInterpretation = {
  startDate: day("2025-01-01"),
  endDate: day("2025-12-31"),
  games: [{ id: "cod", name: "Call of Duty" }],
  moodTags: ["funny"],
  people: ["Sam"],
  phrase: "falling off the map",
};

describe("dateLabel", () => {
  it("names a whole year, a whole month or one day in the fewest words", () => {
    expect(dateLabel(day("2025-01-01"), day("2025-12-31"))).toBe("2025");
    expect(dateLabel(day("2026-10-01"), day("2026-10-31"))).toBe(
      "October 2026",
    );
    expect(dateLabel(day("2024-02-01"), day("2024-02-29"))).toBe(
      "February 2024",
    );
    expect(dateLabel(day("2026-11-10"), day("2026-11-10"))).toBe("10 Nov 2026");
  });

  it("gives other ranges and open ends their days", () => {
    expect(dateLabel(day("2026-11-02"), day("2026-11-08"))).toBe(
      "2 Nov 2026 – 8 Nov 2026",
    );
    expect(dateLabel(day("2026-03-01"), null)).toBe("Since 1 Mar 2026");
    expect(dateLabel(null, day("2026-03-01"))).toBe("Until 1 Mar 2026");
    expect(dateLabel(null, null)).toBeNull();
  });
});

describe("chipsFor", () => {
  it("reads when, the games, the mood, who, then what, each taking only itself away", () => {
    const chips = chipsFor(interpretation);

    expect(chips.map((chip) => chip.label)).toEqual([
      "2025",
      "Call of Duty",
      "funny",
      "With Sam",
      "“falling off the map”",
    ]);
    expect(chips[0].without).toMatchObject({
      startDate: null,
      endDate: null,
      gameCategoryIds: ["cod"],
    });
    expect(chips[1].without.gameCategoryIds).toEqual([]);
    expect(chips[1].without.moodTags).toEqual(["funny"]);
    expect(chips[2].without.moodTags).toEqual([]);
    expect(chips[3].without.people).toEqual([]);
    expect(chips[4].without).toMatchObject({
      phrase: null,
      people: ["Sam"],
    });
  });

  it("has no chips for a search read into nothing", () => {
    expect(
      chipsFor({
        startDate: null,
        endDate: null,
        games: [],
        moodTags: [],
        people: [],
        phrase: null,
      }),
    ).toEqual([]);
  });
});

describe("hasAnyFilter", () => {
  it("is false once every filter is gone", () => {
    expect(
      hasAnyFilter({
        startDate: null,
        endDate: null,
        gameCategoryIds: [],
        moodTags: [],
        people: [],
        phrase: null,
      }),
    ).toBe(false);
    expect(
      hasAnyFilter({
        startDate: null,
        endDate: null,
        gameCategoryIds: [],
        moodTags: [],
        people: [],
        phrase: "clutch",
      }),
    ).toBe(true);
  });
});

describe("the saved search", () => {
  it("comes back with its dates as dates", () => {
    saveClipSearch({
      query: "funny cod 2025",
      filters: chipsFor(interpretation)[4].without,
    });

    const loaded = loadClipSearch();
    expect(loaded?.query).toBe("funny cod 2025");
    expect(loaded?.filters?.startDate?.toISOString()).toBe(
      "2025-01-01T00:00:00.000Z",
    );
    expect(loaded?.filters?.gameCategoryIds).toEqual(["cod"]);
    expect(loaded?.filters?.phrase).toBeNull();

    saveClipSearch(null);
    expect(loadClipSearch()).toBeNull();
  });

  it("ignores something else stored under its key", () => {
    sessionStorage.setItem("reelshelf.clipSearch", "{not json");
    expect(loadClipSearch()).toBeNull();
  });
});
