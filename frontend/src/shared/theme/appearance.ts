/**
 * Light or dark, and the colour scheme (Settings → Appearance). Both are attributes on <html> that
 * styles/reelshelf.css reads: `data-theme` and `data-color-scheme`, the latter absent for Stacks, the default.
 * index.html applies both from local storage before first paint, so keep the keys in sync with it.
 */

export type ReelshelfTheme = "light" | "dark";
export type ThemePreference = ReelshelfTheme | "system";

/** The schemes, in the order Settings shows them; api/Users/ColorSchemes.cs and reelshelf.css name the same ones. */
export const colorSchemes = [
  "stacks",
  "oxblood",
  "ink",
  "walnut",
  "graphite",
] as const;
export type ColorScheme = (typeof colorSchemes)[number];
export const defaultColorScheme: ColorScheme = "stacks";

// Only an explicit pick from a theme toggle is stored; until then the OS setting is followed.
export const themeStorageKey = "reelshelf-theme-preference";
/** The account's scheme, kept here so the first paint is right before /api/me answers. */
export const colorSchemeStorageKey = "reelshelf-color-scheme";
export const darkSchemeQuery = "(prefers-color-scheme: dark)";

export function isColorScheme(value: unknown): value is ColorScheme {
  return (colorSchemes as readonly unknown[]).includes(value);
}

/** What the account's stored value means here: a scheme this build knows, else the default. */
export function toColorScheme(value: unknown): ColorScheme {
  return isColorScheme(value) ? value : defaultColorScheme;
}

export function getStoredTheme(): ReelshelfTheme | null {
  try {
    const stored = window.localStorage.getItem(themeStorageKey);
    return stored === "light" || stored === "dark" ? stored : null;
  } catch {
    return null;
  }
}

export function storeTheme(theme: ReelshelfTheme | null) {
  try {
    if (theme) window.localStorage.setItem(themeStorageKey, theme);
    else window.localStorage.removeItem(themeStorageKey);
  } catch {
    // Storage may be unavailable (private mode); the choice still holds for this page.
  }
}

export function getSystemTheme(): ReelshelfTheme {
  return window.matchMedia(darkSchemeQuery).matches ? "dark" : "light";
}

export function getStoredColorScheme(): ColorScheme {
  try {
    return toColorScheme(window.localStorage.getItem(colorSchemeStorageKey));
  } catch {
    return defaultColorScheme;
  }
}

export function storeColorScheme(scheme: ColorScheme) {
  try {
    if (scheme === defaultColorScheme) {
      window.localStorage.removeItem(colorSchemeStorageKey);
    } else {
      window.localStorage.setItem(colorSchemeStorageKey, scheme);
    }
  } catch {
    // As above.
  }
}

export function applyColorScheme(scheme: ColorScheme) {
  if (scheme === defaultColorScheme) {
    delete document.documentElement.dataset.colorScheme;
  } else {
    document.documentElement.dataset.colorScheme = scheme;
  }
}
