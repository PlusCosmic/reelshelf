import { describe, expect, it } from "vitest";
import { activityCopy } from "@/copy/activity";
import { clipDayLabel, groupClipsByDay } from "./clipDays";

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
