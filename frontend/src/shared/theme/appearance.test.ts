// @vitest-environment jsdom
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  applyColorScheme,
  colorSchemeStorageKey,
  colorSchemes,
  getStoredColorScheme,
  storeColorScheme,
  toColorScheme,
} from "./appearance";

const css = readFileSync(
  resolve(__dirname, "../../styles/reelshelf.css"),
  "utf8",
);
const indexHtml = readFileSync(
  resolve(__dirname, "../../../index.html"),
  "utf8",
);

/** The custom properties set in the block `selector` opens. */
function tokensOf(selector: string): string[] {
  const start = css.indexOf(`${selector} {`);
  expect(start, `reelshelf.css has a ${selector} block`).toBeGreaterThan(-1);
  const body = css.slice(css.indexOf("{", start) + 1, css.indexOf("}", start));
  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map((match) => match[1]);
}

afterEach(() => {
  window.localStorage.clear();
  delete document.documentElement.dataset.colorScheme;
});

describe("colour schemes", () => {
  it("keeps Stacks off the root element and the others on it", () => {
    applyColorScheme("ink");
    expect(document.documentElement.dataset.colorScheme).toBe("ink");
    applyColorScheme("stacks");
    expect(document.documentElement.dataset.colorScheme).toBeUndefined();
  });

  it("remembers a scheme on this browser and forgets the default", () => {
    storeColorScheme("walnut");
    expect(getStoredColorScheme()).toBe("walnut");
    storeColorScheme("stacks");
    expect(window.localStorage.getItem(colorSchemeStorageKey)).toBeNull();
    expect(getStoredColorScheme()).toBe("stacks");
  });

  it("reads anything it doesn't know as the default", () => {
    window.localStorage.setItem(colorSchemeStorageKey, "green");
    expect(getStoredColorScheme()).toBe("stacks");
    expect(toColorScheme(undefined)).toBe("stacks");
    expect(toColorScheme("graphite")).toBe("graphite");
  });

  it("defines every scheme's light and dark tokens in reelshelf.css, as many as Stacks has", () => {
    const stacks = tokensOf(
      '[data-color-scheme="stacks"]:not([data-theme="dark"])',
    );
    for (const scheme of colorSchemes) {
      for (const mode of [':not([data-theme="dark"])', '[data-theme="dark"]']) {
        const tokens = tokensOf(`[data-color-scheme="${scheme}"]${mode}`);
        // Danger, warning and Twitch's purple are shared, so a scheme may leave them to Stacks.
        const shared = ["--danger", "--warning", "--twitch"];
        expect(
          tokens.filter((token) => !shared.includes(token)).sort(),
        ).toEqual(stacks.filter((token) => !shared.includes(token)).sort());
      }
    }
  });

  it("lists every scheme but the default in index.html's first-paint script", () => {
    for (const scheme of colorSchemes.filter((name) => name !== "stacks")) {
      expect(indexHtml).toContain(`"${scheme}"`);
    }
  });
});
