/**
 * Reelshelf's prefix for @pluscosmic/ui's appearance keys in local storage: `reelshelf-theme-preference` and
 * `reelshelf-color-scheme`, the keys Reelshelf has always used, so nobody's choice is lost. vite.config.ts gives it to
 * the first-paint plugin and AppearanceProvider to the provider. No imports here: Vite's config bundles this file
 * but would load @pluscosmic/ui itself from node_modules.
 */
export const appearanceStorageKey = "reelshelf";
