// @vitest-environment jsdom
import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Clip, PlaylistWithDetails } from "@/api-client";
import { ClipPlayerPage } from "./ClipPlayerPage";

const mocks = vi.hoisted(() => ({
  navigate: vi.fn(),
  markViewed: vi.fn(),
  autoplay: false,
  clips: new Map<string, unknown>(),
  playlist: null as unknown,
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
  useNavigate: () => mocks.navigate,
  useRouterState: ({
    select,
  }: {
    select: (router: {
      location: { state: { autoplay?: boolean } };
    }) => unknown;
  }) => select({ location: { state: { autoplay: mocks.autoplay } } }),
}));

vi.mock("@/hooks/queries", () => ({
  useClip: (clipId: string) => ({
    data: mocks.clips.get(clipId),
    isLoading: false,
    isError: false,
  }),
  useMarkAsViewed: () => ({ mutate: mocks.markViewed, isPending: false }),
}));

vi.mock("@/hooks/clips.queries", () => ({
  useClipNeighbours: () => ({ data: undefined }),
  useClipSession: () => ({ data: undefined }),
  useClipsInfinite: () => ({ data: undefined }),
}));

vi.mock("@/shared/services/playlists", () => ({
  fetchPlaylistById: () => Promise.resolve(mocks.playlist),
}));

vi.mock("./useLibraryData", () => ({
  useLibraryData: () => ({ categories: [], categoryTotals: [] }),
}));

vi.mock("./ReelshelfPrimitives", () => ({
  ClipGrid: () => null,
  ClipThumb: () => null,
  PlayerActions: () => null,
  SharedIndicator: () => null,
}));

// The stream itself is the loader's business; here the page only needs the <video> and its events.
vi.mock("@/shared/utils/hlsStream", () => ({
  attachHlsStream: () => () => undefined,
}));

function clip(clipId: string, isViewed = false): Clip {
  return {
    clipId,
    categorySlug: "apex-legends",
    gameCategoryId: "game",
    createdAt: new Date("2026-10-01T20:00:00Z"),
    tags: [],
    isViewed,
    share: { shared: false },
    video: {
      title: `Clip ${clipId}`,
      durationSeconds: 30,
      storageBytes: 1_000,
      thumbnailUrl: `https://cdn.example/${clipId}/thumbnail.jpg`,
      playbackUrl: `https://cdn.example/${clipId}/playlist.m3u8`,
    },
  } as unknown as Clip;
}

function collection(...clips: Clip[]): PlaylistWithDetails {
  return {
    id: "night",
    name: "Friday night",
    clips: clips.map((item, position) => ({
      id: `item-${position}`,
      clipId: item.clipId,
      position,
      clipDetails: item,
    })),
  } as unknown as PlaylistWithDetails;
}

function renderPage(clipId: string, collectionId?: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const view = render(
    <QueryClientProvider client={client}>
      <ClipPlayerPage
        slug="apex-legends"
        clipId={clipId}
        collectionId={collectionId}
      />
    </QueryClientProvider>,
  );
  const video = view.container.querySelector("video");
  if (!video) throw new Error("No video player");
  return { ...view, video };
}

beforeEach(() => {
  mocks.autoplay = false;
  mocks.clips = new Map(
    [clip("a"), clip("b"), clip("c", true)].map((item) => [item.clipId, item]),
  );
  mocks.playlist = collection(
    mocks.clips.get("a") as Clip,
    mocks.clips.get("b") as Clip,
  );
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("ClipPlayerPage", () => {
  it("plays the clip's HLS stream over its thumbnail", () => {
    const { video } = renderPage("a");
    expect(video.getAttribute("poster")).toBe(
      "https://cdn.example/a/thumbnail.jpg",
    );
    expect(video.controls).toBe(true);
    expect(video.autoplay).toBe(false);
  });

  it("moves on to the collection's next clip, set to play, when the video ends", async () => {
    const { video, findAllByText } = renderPage("a", "night");
    await findAllByText("Clip 1 of 2");

    video.dispatchEvent(new Event("ended"));

    expect(mocks.navigate).toHaveBeenCalledWith({
      to: "/games/$slug/$clipId",
      params: { slug: "apex-legends", clipId: "b" },
      search: { collection: "night" },
      state: { autoplay: true },
    });
  });

  it("stops at the end of the collection, and outside one", async () => {
    const last = renderPage("b", "night");
    await last.findAllByText("Clip 2 of 2");
    last.video.dispatchEvent(new Event("ended"));
    cleanup();

    const alone = renderPage("a");
    alone.video.dispatchEvent(new Event("ended"));

    expect(mocks.navigate).not.toHaveBeenCalled();
  });

  it("autoplays a clip the collection moved on to", async () => {
    mocks.autoplay = true;
    const { video } = renderPage("b", "night");
    await waitFor(() => expect(video.autoplay).toBe(true));
  });

  it("marks the clip watched once it loads, once", () => {
    const { video } = renderPage("a");
    expect(mocks.markViewed).not.toHaveBeenCalled();

    video.dispatchEvent(new Event("loadedmetadata"));
    video.dispatchEvent(new Event("loadedmetadata"));

    expect(mocks.markViewed).toHaveBeenCalledTimes(1);
    expect(mocks.markViewed).toHaveBeenCalledWith("a");
  });

  it("leaves a watched clip alone", () => {
    const { video } = renderPage("c");
    video.dispatchEvent(new Event("loadedmetadata"));
    expect(mocks.markViewed).not.toHaveBeenCalled();
  });

  it("swaps the stream on the same element for the next clip and marks that one too", () => {
    const view = renderPage("a");
    view.video.dispatchEvent(new Event("loadedmetadata"));
    const client = new QueryClient();
    view.rerender(
      <QueryClientProvider client={client}>
        <ClipPlayerPage slug="apex-legends" clipId="b" />
      </QueryClientProvider>,
    );
    expect(view.container.querySelector("video")).toBe(view.video);
    view.video.dispatchEvent(new Event("loadedmetadata"));

    expect(mocks.markViewed.mock.calls).toEqual([["a"], ["b"]]);
  });
});
