import { describe, expect, it } from "vitest";
import { activityCopy } from "@/copy/activity";
import {
  clipDayLabel,
  dayRangeBounds,
  dayRangeLabel,
  groupClipsByDay,
} from "./clipDays";

const now = new Date(2026, 9, 30, 21, 0);
const at = (month: number, day: number, hour = 12, year = 2026) =>
  new Date(year, month, day, hour);

describe("clipDayLabel", () => {
  it("names today and yesterday", () => {
    expect(clipDayLabel(at(9, 30, 0), now)).toBe(activityCopy.picker.today);
    expect(clipDayLabel(at(9, 29, 23), now)).toBe(
      activityCopy.picker.yesterday,
    );
  });

  it("names the weekday within the last week, then the date", () => {
    expect(clipDayLabel(at(9, 26), now)).toBe(
      new Intl.DateTimeFormat(undefined, { weekday: "long" }).format(at(9, 26)),
    );
    expect(clipDayLabel(at(9, 23), now)).toBe(
      new Intl.DateTimeFormat(undefined, {
        day: "numeric",
        month: "long",
      }).format(at(9, 23)),
    );
  });

  it("adds the year to another year's dates", () => {
    expect(clipDayLabel(at(11, 31, 12, 2025), now)).toContain("2025");
  });
});

describe("groupClipsByDay", () => {
  it("keeps the clips' order and starts a group at each new day", () => {
    const clips = [
      { id: "a", createdAt: at(9, 30, 20) },
      { id: "b", createdAt: at(9, 30, 9) },
      { id: "c", createdAt: at(9, 28, 22) },
      { id: "d", createdAt: at(9, 28, 1) },
    ];
    const days = groupClipsByDay(clips, now);
    expect(days.map((day) => day.clips.map((clip) => clip.id))).toEqual([
      ["a", "b"],
      ["c", "d"],
    ]);
    expect(days[0].label).toBe(activityCopy.picker.today);
  });

  it("works oldest first too", () => {
    const clips = [
      { createdAt: at(9, 28, 1) },
      { createdAt: at(9, 28, 22) },
      { createdAt: at(9, 30, 9) },
    ];
    expect(groupClipsByDay(clips, now).map((day) => day.clips.length)).toEqual([
      2, 1,
    ]);
  });
});

describe("dayRangeBounds", () => {
  it("covers the first day from midnight to the last moment of the last day", () => {
    const { start, end } = dayRangeBounds("2026-10-12", "2026-10-31");
    expect(start).toEqual(new Date(2026, 9, 12));
    expect(end).toEqual(new Date(new Date(2026, 10, 1).getTime() - 1));
  });

  it("covers one whole day when both ends are the same day", () => {
    const { start, end } = dayRangeBounds("2026-10-31", "2026-10-31");
    expect(start).toEqual(new Date(2026, 9, 31));
    expect(end!.getTime() - start!.getTime()).toBe(86_400_000 - 1);
  });

  it("leaves an empty or malformed end open", () => {
    expect(dayRangeBounds("", "2026-10-31").start).toBeNull();
    expect(dayRangeBounds("2026-10-12", "").end).toBeNull();
    expect(dayRangeBounds("nonsense", "")).toEqual({ start: null, end: null });
  });

  it("swaps days given the wrong way round", () => {
    const { start, end } = dayRangeBounds("2026-10-31", "2026-10-12");
    expect(start).toEqual(new Date(2026, 9, 12));
    expect(end).toEqual(new Date(new Date(2026, 10, 1).getTime() - 1));
  });
});

describe("dayRangeLabel", () => {
  const short = new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
  });

  it("names both ends, either end alone, or nothing", () => {
    expect(dayRangeLabel("2026-10-12", "2026-10-31", now)).toBe(
      activityCopy.picker.dates.between(
        short.format(at(9, 12)),
        short.format(at(9, 31)),
      ),
    );
    expect(dayRangeLabel("2026-10-12", "", now)).toBe(
      activityCopy.picker.dates.since(short.format(at(9, 12))),
    );
    expect(dayRangeLabel("", "2026-10-31", now)).toBe(
      activityCopy.picker.dates.until(short.format(at(9, 31))),
    );
    expect(dayRangeLabel("", "", now)).toBeNull();
  });
});
