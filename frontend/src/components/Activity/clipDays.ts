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
