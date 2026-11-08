import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { appearanceKeys, colorSchemes } from "@pluscosmic/ui";
import { describe, expect, it } from "vitest";
import { appearanceStorageKey } from "./storageKey";

const css = readFileSync(
  resolve(__dirname, "../../styles/reelshelf.css"),
  "utf8",
);

/** The custom properties set in the block `selector` opens in `source`. */
function tokensOf(source: string, selector: string): string[] {
  const start = source.indexOf(`${selector} {`);
  expect(start, `a ${selector} block`).toBeGreaterThan(-1);
  const body = source.slice(
    source.indexOf("{", start) + 1,
    source.indexOf("}", start),
  );
  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map((match) => match[1]);
}

describe("appearance", () => {
  it("keeps the local storage keys Reelshelf has always used, so nobody's choice is lost", () => {
    expect(appearanceKeys(appearanceStorageKey)).toEqual({
      theme: "reelshelf-theme-preference",
      colorScheme: "reelshelf-color-scheme",
    });
  });

  it("defines Reelshelf's own tokens in every scheme, light and dark, as many as Stacks has", () => {
    const stacks = tokensOf(
      css,
      '[data-color-scheme="stacks"]:not([data-theme="dark"])',
    );
    // Twitch's purple is shared, so a scheme may leave it to Stacks.
    const own = (tokens: string[]) =>
      tokens.filter((token) => token !== "--twitch").sort();
    for (const scheme of colorSchemes) {
      for (const mode of [':not([data-theme="dark"])', '[data-theme="dark"]']) {
        expect(
          own(tokensOf(css, `[data-color-scheme="${scheme}"]${mode}`)),
          `${scheme} ${mode}`,
        ).toEqual(own(stacks));
      }
    }
  });
});
