import { activityCopy } from "@/copy/activity";

export type ClipDay<T> = { key: string; label: string; clips: T[] };

const weekdayFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: "long",
});
const dateFormatter = new Intl.DateTimeFormat(undefined, {
  day: "numeric",
  month: "long",
});
const datedYearFormatter = new Intl.DateTimeFormat(undefined, {
  day: "numeric",
  month: "long",
  year: "numeric",
});
const shortDateFormatter = new Intl.DateTimeFormat(undefined, {
  day: "numeric",
  month: "short",
});
const shortDatedYearFormatter = new Intl.DateTimeFormat(undefined, {
  day: "numeric",
  month: "short",
  year: "numeric",
});
const timeFormatter = new Intl.DateTimeFormat(undefined, {
  hour: "numeric",
  minute: "2-digit",
});

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function dayKey(date: Date) {
  return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
}

/** "Today", "Yesterday", a weekday within the last week, else the date (with its year when it isn't this one). */
export function clipDayLabel(date: Date, now: Date) {
  const days = Math.round(
    (startOfDay(now).getTime() - startOfDay(date).getTime()) / 86_400_000,
  );
  if (days === 0) return activityCopy.picker.today;
  if (days === 1) return activityCopy.picker.yesterday;
  if (days > 1 && days < 7) return weekdayFormatter.format(date);
  return date.getFullYear() === now.getFullYear()
    ? dateFormatter.format(date)
    : datedYearFormatter.format(date);
}

export function clipTime(date: Date) {
  return timeFormatter.format(date);
}

/**
 * Splits clips already in date order into runs of the same local day. A day that carries on onto the next
 * page joins the run it started, since the pages are grouped together.
 */
export function groupClipsByDay<T extends { createdAt: Date }>(
  clips: readonly T[],
  now: Date,
): ClipDay<T>[] {
  const days: ClipDay<T>[] = [];
  for (const clip of clips) {
    const key = dayKey(clip.createdAt);
    const last = days.at(-1);
    if (last?.key === key) last.clips.push(clip);
    else
      days.push({
        key,
        label: clipDayLabel(clip.createdAt, now),
        clips: [clip],
      });
  }
  return days;
}

/** A date input's value ("2026-10-31") as the local midnight it starts at, or null when empty or malformed. */
function parseDay(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const day = new Date(
    Number(match[1]),
    Number(match[2]) - 1,
    Number(match[3]),
  );
  return Number.isNaN(day.getTime()) ? null : day;
}

/**
 * The instants a range of local days covers, for the API: from the first day's midnight to the last moment of
 * the last day, both inclusive. Either day may be empty to leave that end open, and days given the wrong way
 * round are swapped, so a range is never empty by mistake.
 */
export function dayRangeBounds(from: string, to: string) {
  let first = parseDay(from);
  let last = parseDay(to);
  if (first && last && first > last) [first, last] = [last, first];
  const end = last
    ? new Date(
        new Date(
          last.getFullYear(),
          last.getMonth(),
          last.getDate() + 1,
        ).getTime() - 1,
      )
    : null;
  return { start: first, end };
}

/** The range as the picker's bar names it: "12 Oct – 31 Oct", "From 12 Oct", "Until 31 Oct", or null. */
export function dayRangeLabel(from: string, to: string, now: Date) {
  const { start, end } = dayRangeBounds(from, to);
  const format = (date: Date) =>
    (date.getFullYear() === now.getFullYear()
      ? shortDateFormatter
      : shortDatedYearFormatter
    ).format(date);
  if (start && end)
    return activityCopy.picker.dates.between(format(start), format(end));
  if (start) return activityCopy.picker.dates.since(format(start));
  if (end) return activityCopy.picker.dates.until(format(end));
  return null;
}
