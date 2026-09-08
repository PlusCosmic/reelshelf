// @vitest-environment jsdom
import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { GameCategoryResponse, TwitchClipsResponse } from "@/api-client";
import { TwitchClipsPanel } from "./TwitchClipsPanel";

const twitchMocks = vi.hoisted(() => ({
  fetchTwitchClips: vi.fn(),
  importTwitchClip: vi.fn(),
}));

const playlistMocks = vi.hoisted(() => ({
  ensureGamingSessionPlaylist: vi.fn(),
}));

const authMocks = vi.hoisted(() => ({
  startLink: vi.fn(),
}));

const categories: GameCategoryResponse[] = [
  {
    id: "apex",
    name: "Apex Legends",
    slug: "apex-legends",
    coverUrl: null,
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
  },
];

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/">{children}</a>,
  useBlocker: vi.fn(),
}));

vi.mock("@/hooks/queries", async () => {
  const twitchQueries = await vi.importActual<
    typeof import("@/hooks/twitch.queries")
  >("@/hooks/twitch.queries");
  return {
    ...twitchQueries,
    useCategories: () => ({ data: categories, isLoading: false }),
    useAddGameFromIgdb: () => ({ mutate: vi.fn(), isPending: false }),
  };
});

vi.mock("@/shared/services/twitch", () => twitchMocks);
vi.mock("@/shared/services/playlists", () => playlistMocks);
vi.mock("@/shared/services/auth", () => authMocks);

function readyPage(
  overrides: Partial<TwitchClipsResponse> = {},
): TwitchClipsResponse {
  return {
    state: "Ready",
    twitchLogin: "harry_tv",
    cursor: null,
    clips: [
      {
        id: "twitch-1",
        title: "Squad wipe",
        url: "https://clips.twitch.tv/twitch-1",
        thumbnailUrl: null,
        durationSeconds: 28,
        viewCount: 40,
        createdAt: new Date(2026, 8, 5, 22, 0),
        creatorName: "harry_tv",
        gameName: "Apex Legends",
        igdbId: 114795,
        suggestedCategoryId: "apex",
        alreadyImported: false,
      },
      {
        id: "twitch-2",
        title: "Old one",
        url: "https://clips.twitch.tv/twitch-2",
        thumbnailUrl: null,
        durationSeconds: 15,
        viewCount: 3,
        createdAt: new Date(2026, 8, 1, 12, 0),
        creatorName: "harry_tv",
        gameName: "Apex Legends",
        igdbId: 114795,
        suggestedCategoryId: "apex",
        alreadyImported: true,
      },
      {
        id: "twitch-3",
        title: "Unknown game",
        url: "https://clips.twitch.tv/twitch-3",
        thumbnailUrl: null,
        durationSeconds: 15,
        viewCount: 3,
        createdAt: new Date(2026, 8, 2, 12, 0),
        creatorName: "harry_tv",
        gameName: "Valorant",
        igdbId: 126459,
        suggestedCategoryId: null,
        alreadyImported: false,
      },
    ],
    ...overrides,
  };
}

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <TwitchClipsPanel notice={null} />
    </QueryClientProvider>,
  );
}

describe("TwitchClipsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    playlistMocks.ensureGamingSessionPlaylist.mockResolvedValue({
      id: "session-playlist",
    });
  });

  afterEach(() => {
    cleanup();
  });

  it("offers to link Twitch when the account has no Twitch identity", async () => {
    twitchMocks.fetchTwitchClips.mockResolvedValue(
      readyPage({ state: "NotLinked", twitchLogin: null, clips: [] }),
    );
    renderPanel();

    fireEvent.click(
      await screen.findByRole("button", { name: /link twitch/i }),
    );

    expect(authMocks.startLink).toHaveBeenCalledWith(
      "twitch",
      expect.stringContaining("/upload?source=twitch"),
    );
  });

  it("asks to reconnect when the stored token cannot download clips", async () => {
    twitchMocks.fetchTwitchClips.mockResolvedValue(
      readyPage({ state: "NeedsAuthorization", clips: [] }),
    );
    renderPanel();

    expect(
      await screen.findByText(/reconnect twitch to allow clip downloads/i),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /reconnect twitch/i }));
    expect(authMocks.startLink).toHaveBeenCalledWith(
      "twitch",
      expect.any(String),
    );
  });

  it("lists clips, pre-assigns known games, and imports the selection into a session collection", async () => {
    twitchMocks.fetchTwitchClips.mockResolvedValue(readyPage());
    twitchMocks.importTwitchClip.mockImplementation(
      async ({ clipId }: { clipId: string }) => ({
        clipId: `library-${clipId}`,
        videoId: `video-${clipId}`,
        twitchClipId: clipId,
        categoryId: "apex",
        createdAt: new Date(),
      }),
    );
    renderPanel();

    expect(await screen.findByDisplayValue("Squad wipe")).toBeTruthy();
    expect(screen.getByText("@harry_tv")).toBeTruthy();
    expect(screen.getAllByText("in library")).toHaveLength(1);
    expect(screen.getByText("needs game")).toBeTruthy();
    expect(
      screen.getByRole("button", { name: /add valorant \(1\)/i }),
    ).toBeTruthy();
    expect(screen.getByText("1 of 3 selected")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /add 1 to library/i }));

    await waitFor(() =>
      expect(twitchMocks.importTwitchClip).toHaveBeenCalledWith({
        clipId: "twitch-1",
        categoryId: "apex",
        title: "Squad wipe",
      }),
    );
    expect(twitchMocks.importTwitchClip).toHaveBeenCalledTimes(1);

    await waitFor(() =>
      expect(playlistMocks.ensureGamingSessionPlaylist).toHaveBeenCalledWith(
        expect.objectContaining({
          categoryId: "apex",
          clipIds: ["library-twitch-1"],
          sessionDate: new Date("2026-09-05T00:00:00"),
        }),
      ),
    );
    expect(await screen.findByText("saved")).toBeTruthy();
    expect(screen.getByText("Open collection")).toBeTruthy();
  });

  it("shows the API's reason when an import fails and lets the user retry", async () => {
    twitchMocks.fetchTwitchClips.mockResolvedValue(readyPage());
    twitchMocks.importTwitchClip.mockRejectedValueOnce(
      new Error(
        "The clip could not be copied from Twitch. Try again in a moment.",
      ),
    );
    renderPanel();

    await screen.findByDisplayValue("Squad wipe");
    fireEvent.click(screen.getByRole("button", { name: /add 1 to library/i }));

    expect(
      await screen.findByText(/could not be copied from twitch/i),
    ).toBeTruthy();
    expect(playlistMocks.ensureGamingSessionPlaylist).not.toHaveBeenCalled();

    twitchMocks.importTwitchClip.mockResolvedValueOnce({
      clipId: "library-twitch-1",
      videoId: "video",
      twitchClipId: "twitch-1",
      categoryId: "apex",
      createdAt: new Date(),
    });
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText("saved")).toBeTruthy();
    expect(twitchMocks.importTwitchClip).toHaveBeenCalledTimes(2);
  });
});
