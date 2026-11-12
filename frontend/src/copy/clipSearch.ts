import { commonCopy } from "./common";

const months = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
];

/** A day as "3 Mar 2026". */
const day = (date: Date) =>
  `${date.getUTCDate()} ${months[date.getUTCMonth()].slice(0, 3)} ${date.getUTCFullYear()}`;

/**
 * Searching the library by description: the entry under the shelf, the filters the search was read into (each a chip
 * that takes it away), and the results.
 */
export const clipSearchCopy = {
  label: "Describe a clip",
  hint: "Say what happened, the game, who was there or when, and Reelshelf finds the clips that fit.",
  placeholder: "Funny clips from Call of Duty last month",
  submit: "Find clips",
  searching: "Finding clips…",
  heading: "Clips that fit your description",
  readAs: "Searched for",
  clear: "Back to all clips",
  remove: (chip: string) => `Remove ${chip}`,
  nothingLeft: "Every filter is gone. Describe the clip again to search.",
  count: (count: number) =>
    count === 0 ? "No clips fit" : `${commonCopy.clips(count)} fit`,
  noResults:
    "No clips fit that. Take a filter away, or describe the clip another way.",
  failed: "That search didn't work. Try again in a moment.",
  tooLong: "Keep it shorter: a sentence is plenty.",
  chips: {
    year: (year: number) => `${year}`,
    month: (date: Date) =>
      `${months[date.getUTCMonth()]} ${date.getUTCFullYear()}`,
    day,
    range: (start: Date, end: Date) => `${day(start)} – ${day(end)}`,
    since: (start: Date) => `Since ${day(start)}`,
    until: (end: Date) => `Until ${day(end)}`,
    person: (name: string) => `With ${name}`,
    phrase: (phrase: string) => `“${phrase}”`,
  },
};
