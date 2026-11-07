import { plural } from "./plural";

/** The desktop uploader's Microsoft Store listing, by its Store ID. */
export const uploaderStoreUrl =
  "https://apps.microsoft.com/detail/9PC7WVJ4H5MD";

const dayFormatter = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  year: "numeric",
});

/** "6 Nov 2026". */
export const formatDeviceDate = (date: Date) => dayFormatter.format(date);

/** "just now", "4 minutes ago", "3 hours ago", then the date. */
export function formatLastUsed(date: Date, now: Date = new Date()) {
  const minutes = Math.floor((now.getTime() - date.getTime()) / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${plural(minutes, "minute", "minutes")} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${plural(hours, "hour", "hours")} ago`;
  return formatDeviceDate(date);
}

/** Signing in the desktop uploader on /device, the devices in Settings, and the uploader's links. */
export const deviceCopy = {
  /** /device: approving a sign-in the uploader asked for. */
  approve: {
    eyebrow: "Sign in a device",
    title: (device: string) => `Let "${device}" add clips to your library?`,
    body: "Check this matches the code in the uploader. The device can see your books and add clips to them. It can't change or delete anything else. Remove it any time in Settings → Devices.",
    allow: "Allow",
    deny: "Don't allow",
    working: "Saving…",
    loading: "Looking up the code…",
    codeLabel: "Enter the code shown in the uploader",
    codePlaceholder: "ABCD-EFGH",
    continue: "Continue",
    unknown:
      "That code didn't match a sign-in, so nothing changed. Check the code in the uploader, or get a new one there.",
    allowed: (device: string) =>
      `${device} is signed in. You can close this tab.`,
    denied: (device: string) => `${device} wasn't signed in. Nothing changed.`,
    failed: "That didn't go through, so nothing changed. Try again.",
    loadFailed: "The sign-in didn't load. Try again.",
  },

  /** Settings → Devices. */
  devices: {
    loading: "Loading devices…",
    loadFailed: "Your devices didn't load.",
    empty: "No devices yet.",
    used: (added: Date, lastUsed: Date, now?: Date) =>
      `Added ${formatDeviceDate(added)} · last used ${formatLastUsed(lastUsed, now)}`,
    remove: "Remove",
    confirm: (device: string) =>
      `Remove ${device}? It stops uploading at once.`,
    cancel: "Cancel",
    removing: "Removing…",
    removeFailed: (device: string) => `${device} wasn't removed. Try again.`,
    store: "Upload clips automatically while you play.",
    storeLink: "Get the uploader from the Microsoft Store",
  },

  /** Beside the library's drop-on-a-book prompt, on Windows desktop browsers. */
  libraryHint: "Or upload automatically while you play",
} as const;
