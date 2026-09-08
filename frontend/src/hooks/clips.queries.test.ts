import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import type { Clip, PagedClipsResponse } from "@/api-client";
import { markClipViewedInCachedPages } from "./clips.queries";

function clip(clipId: string, isViewed: boolean): Clip {
  return { clipId, isViewed } as Clip;
}

function page(...clips: Clip[]): PagedClipsResponse {
  return { clips, totalClips: clips.length, totalPages: 1 };
}

describe("markClipViewedInCachedPages", () => {
  it("clears the New badge on every cached page holding the clip", () => {
    const queryClient = new QueryClient();
    const key = ["clips", "list", { categoryId: "game-1" }];
    queryClient.setQueryData(key, {
      pageParams: [1, 2],
      pages: [
        page(clip("a", false), clip("b", false)),
        page(clip("c", false), clip("a", false)),
      ],
    });

    markClipViewedInCachedPages(queryClient, "a");

    const pages = queryClient.getQueryData<{ pages: PagedClipsResponse[] }>(
      key,
    )!.pages;
    expect(pages[0].clips.map((item) => item.isViewed)).toEqual([true, false]);
    expect(pages[1].clips.map((item) => item.isViewed)).toEqual([false, true]);
  });

  it("patches every list query, not just one filter's", () => {
    const queryClient = new QueryClient();
    const unfiltered = ["clips", "list", { categoryId: "game-1" }];
    const tagged = ["clips", "list", { categoryId: "game-1", tag: "ace" }];
    queryClient.setQueryData(unfiltered, {
      pageParams: [1],
      pages: [page(clip("a", false))],
    });
    queryClient.setQueryData(tagged, {
      pageParams: [1],
      pages: [page(clip("a", false))],
    });

    markClipViewedInCachedPages(queryClient, "a");

    for (const key of [unfiltered, tagged]) {
      const data = queryClient.getQueryData<{ pages: PagedClipsResponse[] }>(
        key,
      )!;
      expect(data.pages[0].clips[0].isViewed).toBe(true);
    }
  });

  it("leaves an empty cache alone", () => {
    const queryClient = new QueryClient();
    expect(() => markClipViewedInCachedPages(queryClient, "a")).not.toThrow();
  });
});
