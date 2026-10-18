// @vitest-environment jsdom
import { cleanup, fireEvent, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Clip } from "@/api-client";
import { ClipThumb } from "./ClipViews";

vi.mock("@/components/AddClips/UploadsProvider", () => ({
  useUploadForClip: () => null,
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
