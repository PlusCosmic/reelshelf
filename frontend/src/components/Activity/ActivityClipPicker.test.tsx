// @vitest-environment jsdom
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ActivityClip } from "@/shared/services/discordActivity";
import { ActivityClipPicker } from "./ActivityClipPicker";

const api = vi.hoisted(() => ({
  fetchOwnClips: vi.fn(),
  fetchOwnGames: vi.fn(),
}));

vi.mock("@/shared/services/discordActivity", () => ({
  fetchOwnClips: api.fetchOwnClips,
  fetchOwnGames: api.fetchOwnGames,
  activityThumbnailUrl: () => null,
}));

function clip(id: string): ActivityClip {
  return {
    clipId: id,
    videoId: id,
    title: `Clip ${id}`,
    game: "Apex Legends",
    durationSeconds: 30,
    createdAt: new Date("2026-11-01T12:00:00Z"),
    ready: true,
    media: null,
  } as unknown as ActivityClip;
}

beforeEach(() => {
  api.fetchOwnGames.mockResolvedValue([]);
  api.fetchOwnClips.mockImplementation(
    (_token: string, options: { page?: number }) =>
      Promise.resolve({
        clips: [clip(String(options.page ?? 1))],
        hasMore: true,
      }),
  );
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("ActivityClipPicker", () => {
  it("asks for the room token on every request, so a renewed one is used without reloading the list", async () => {
    let token = "first-token";
    render(
      <ActivityClipPicker
        getRoomToken={() => token}
        queuedClipIds={new Set()}
        onPick={() => undefined}
      />,
    );

    await screen.findByRole("button", { name: "Load more" });
    expect(api.fetchOwnGames).toHaveBeenCalledWith("first-token");
    expect(api.fetchOwnClips).toHaveBeenCalledTimes(1);
    expect(api.fetchOwnClips).toHaveBeenLastCalledWith(
      "first-token",
      expect.objectContaining({ page: 1 }),
    );

    // The room renews its token; the picker keeps its list and uses the new token next time.
    token = "renewed-token";
    fireEvent.click(screen.getByRole("button", { name: "Load more" }));

    await waitFor(() => expect(api.fetchOwnClips).toHaveBeenCalledTimes(2));
    expect(api.fetchOwnClips).toHaveBeenLastCalledWith(
      "renewed-token",
      expect.objectContaining({ page: 2 }),
    );

    fireEvent.click(screen.getByRole("button", { name: "Oldest" }));

    await waitFor(() => expect(api.fetchOwnClips).toHaveBeenCalledTimes(3));
    expect(api.fetchOwnClips).toHaveBeenLastCalledWith(
      "renewed-token",
      expect.objectContaining({ page: 1, order: "oldest" }),
    );
    expect(api.fetchOwnGames).toHaveBeenCalledTimes(1);
  });
});
