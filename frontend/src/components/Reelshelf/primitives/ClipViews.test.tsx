// @vitest-environment jsdom
import { cleanup, fireEvent, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import type { Clip } from "@/api-client";
import { ClipGrid, ClipThumb } from "./ClipViews";

vi.mock("@/components/AddClips/UploadsProvider", () => ({
  useUploadForClip: () => null,
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({
    children,
    className,
  }: {
    children: ReactNode;
    className?: string;
  }) => <a className={className}>{children}</a>,
}));

afterEach(() => cleanup());

function clip(thumbnailUrl: string | null): Clip {
  return {
    clipId: "a",
    tags: [],
    share: { shared: false },
    video: {
      title: "Clip",
      status: "processing",
      encodeProgress: 0,
      durationSeconds: 0,
      thumbnailUrl,
      sources: [],
    },
  } as unknown as Clip;
}

describe("ClipThumb", () => {
  it("asks for no image until the clip has a thumbnail", () => {
    const view = render(<ClipThumb clip={clip(null)} />);
    expect(view.container.querySelector("img")).toBeNull();
  });

  it("shows a thumbnail that arrives later, even after an earlier one failed", () => {
    const view = render(
      <ClipThumb clip={clip("https://cdn.example/old.jpg")} />,
    );
    const failed = view.container.querySelector("img")!;
    fireEvent.error(failed);
    expect(failed.style.display).toBe("none");

    view.rerender(<ClipThumb clip={clip("https://cdn.example/new.jpg")} />);
    const fresh = view.container.querySelector("img")!;
    expect(fresh.getAttribute("src")).toBe("https://cdn.example/new.jpg");
    expect(fresh.style.display).toBe("");
  });
});

function rowClip(clipId: string, legend: string | null): Clip {
  return {
    ...clip(null),
    clipId,
    categorySlug: "apex-legends",
    isViewed: true,
    legend,
  } as Clip;
}

describe("ClipGrid legend column", () => {
  const clips = [rowClip("a", "Mad Maggie"), rowClip("b", null)];

  it("shows the legend in one game's list, keeping the column for a clip without one", () => {
    const view = render(
      <ClipGrid
        clips={clips}
        categories={[]}
        variant="filmstrip"
        showGame={false}
      />,
    );
    const cells = view.container.querySelectorAll(".rs-row-legend");
    expect(cells).toHaveLength(2);
    expect(cells[0].textContent).toContain("Mad Maggie");
    expect(cells[0].querySelector("img")?.getAttribute("src")).toBe(
      "/apex-legends/mad-maggie.png",
    );
    expect(cells[1].textContent).toBe("");
  });

  it("leaves the legend out of a list that shows each clip's game", () => {
    const view = render(
      <ClipGrid clips={clips} categories={[]} variant="filmstrip" />,
    );
    expect(view.container.querySelector(".has-legend")).toBeNull();
    expect(view.container.textContent).not.toContain("Mad Maggie");
  });

  it("adds no column to a game whose clips have no legend", () => {
    const view = render(
      <ClipGrid
        clips={[rowClip("c", null)]}
        categories={[]}
        variant="filmstrip"
        showGame={false}
      />,
    );
    expect(view.container.querySelector(".rs-row-legend")).toBeNull();
  });
});
