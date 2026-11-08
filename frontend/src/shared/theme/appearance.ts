/**
 * Light or dark, and the colour scheme, from @pluscosmic/ui: attributes on <html> (`data-theme`, and
 * `data-color-scheme`, absent for Stacks) that its tokens.css and styles/reelshelf.css read. The `appearance` Vite
 * plugin (vite.config.ts) applies both from local storage before first paint. The scheme list is the package's; the
 * API's ColorSchemes.cs and the `app_user.color_scheme` constraint name the same schemes.
 */
export {
  type ColorScheme,
  type Theme,
  type ThemePreference,
  colorSchemes,
  defaultColorScheme,
  isColorScheme,
  toColorScheme,
} from "@pluscosmic/ui";
export { appearanceStorageKey } from "./storageKey";
