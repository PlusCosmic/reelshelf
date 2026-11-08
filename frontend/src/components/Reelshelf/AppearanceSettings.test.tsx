// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AppearanceProvider } from "@/shared/theme/AppearanceProvider";
import { AppearanceSettings } from "./AppearanceSettings";

const mocks = vi.hoisted(() => ({ save: vi.fn() }));

vi.mock("@/hooks/auth.queries", () => ({
  useSetColorScheme: () => ({ mutate: mocks.save, isPending: false }),
}));

beforeAll(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia;
});

afterEach(() => {
  cleanup();
  mocks.save.mockReset();
  window.localStorage.clear();
});

function renderSettings() {
  render(
    <AppearanceProvider>
      <AppearanceSettings />
    </AppearanceProvider>,
  );
}

describe("AppearanceSettings", () => {
  it("offers every colour scheme, each previewing its own", () => {
    renderSettings();
    const options = screen.getAllByRole("radio");
    expect(options.map((option) => (option as HTMLInputElement).value)).toEqual(
      ["stacks", "oxblood", "ink", "walnut", "graphite"],
    );
    expect(
      (screen.getByRole("radio", { name: /Stacks/ }) as HTMLInputElement)
        .checked,
    ).toBe(true);
    expect(
      document.querySelector('.rs-scheme-preview[data-color-scheme="ink"]'),
    ).not.toBeNull();
  });

  it("saves a scheme to the account when one is picked", () => {
    renderSettings();
    fireEvent.click(screen.getByRole("radio", { name: /Walnut/ }));
    expect(mocks.save).toHaveBeenCalledWith("walnut", expect.anything());
  });

  it("keeps light or dark on this browser", () => {
    renderSettings();
    fireEvent.click(screen.getByRole("button", { name: "Dark" }));
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(window.localStorage.getItem("reelshelf-theme-preference")).toBe(
      "dark",
    );
    fireEvent.click(screen.getByRole("button", { name: "Match system" }));
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(
      window.localStorage.getItem("reelshelf-theme-preference"),
    ).toBeNull();
  });
});
