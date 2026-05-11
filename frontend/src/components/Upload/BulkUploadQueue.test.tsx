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
import type { GameCategoryResponse, PlaylistSummary } from "@/api-client";
import { BulkUploadQueue } from "./BulkUploadQueue";

const clipUploadMocks = vi.hoisted(() => ({
  calculateClipUploadMd5: vi.fn(),
  createPreparedClipUpload: vi.fn(),
  createTusClipUpload: vi.fn(),
  prepareClipUpload: vi.fn(),
  uploadErrorMessage: vi.fn((error: unknown) =>
    error instanceof Error ? error.message : "The upload failed.",
  ),
}));

const serviceMocks = vi.hoisted(() => ({
  addClipsToPlaylist: vi.fn(),
  addTagToVideo: vi.fn(),
  ensureGamingSessionPlaylist: vi.fn(),
  fetchPlaylists: vi.fn(),
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
  {
    id: "valorant",
    name: "Valorant",
    slug: "valorant",
    coverUrl: null,
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
  },
];

const playlists: PlaylistSummary[] = [
  {
    id: "best-of",
    name: "Best of 2026",
    description: null,
    creatorUserId: "user-1",
    createdAt: new Date("2026-01-01T00:00:00Z"),
    updatedAt: new Date("2026-01-01T00:00:00Z"),
    clipCount: 0,
    collaboratorCount: 0,
  },
];

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
  useBlocker: vi.fn(),
}));

vi.mock("@/hooks/queries", () => ({
  useCategories: () => ({
    data: categories,
    isLoading: false,
  }),
}));

vi.mock("@/shared/services/clips", () => ({
  addTagToVideo: serviceMocks.addTagToVideo,
}));

vi.mock("@/shared/services/playlists", () => ({
  addClipsToPlaylist: serviceMocks.addClipsToPlaylist,
  ensureGamingSessionPlaylist: serviceMocks.ensureGamingSessionPlaylist,
  fetchPlaylists: serviceMocks.fetchPlaylists,
}));

vi.mock("@/utils/clipUpload", () => clipUploadMocks);

describe("BulkUploadQueue", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clipUploadMocks.calculateClipUploadMd5.mockImplementation(
      async (file: File) => `hash-${file.name}`,
    );
    clipUploadMocks.createPreparedClipUpload.mockImplementation(
      async ({ md5Hash }: { md5Hash: string }) => ({
        clipId: `clip-${md5Hash}`,
        collectionId: "bunny-collection",
        expiration: 1,
        libraryId: "library",
        signature: "signature",
        videoId: `video-${md5Hash}`,
      }),
    );
    clipUploadMocks.createTusClipUpload.mockImplementation(
      (options: {
        onError: (error: Error) => void;
        onProgress: (uploaded: number, total: number) => void;
        onSuccess: () => void;
      }) => ({
        abort: vi.fn(),
        findPreviousUploads: vi.fn(async () => []),
        start: vi.fn(() => options.onProgress(1, 4)),
      }),
    );
    serviceMocks.addClipsToPlaylist.mockResolvedValue({});
    serviceMocks.addTagToVideo.mockResolvedValue(undefined);
    serviceMocks.ensureGamingSessionPlaylist.mockResolvedValue({
      id: "session-playlist",
      clips: [],
      collaboratorCount: 0,
      collaborators: [],
      createdAt: new Date("2026-01-01T00:00:00Z"),
      creatorUserId: "user-1",
      description: null,
      name: "Apex Legends - 2026-05-04",
      updatedAt: new Date("2026-01-01T00:00:00Z"),
    });
    serviceMocks.fetchPlaylists.mockResolvedValue(playlists);
  });

  afterEach(() => {
    cleanup();
  });

  it("renders queued files as grouped editable rows", async () => {
    renderQueue();

    const fileInput = screen.getByLabelText("Choose video files");
    const apexFile = videoFile("Apex Legends/session_2026-05-04_2147.mp4");
    const unmatchedFile = videoFile("screen_recording_001.mp4");

    fireEvent.change(fileInput, {
      target: { files: [apexFile, unmatchedFile] },
    });

    expect(
      await screen.findByDisplayValue("session_2026-05-04_2147"),
    ).toBeTruthy();
    expect(screen.getByDisplayValue("screen_recording_001")).toBeTruthy();
    expect(screen.getAllByText("Apex Legends").length).toBeGreaterThan(0);
    expect(screen.getByText("Needs game")).toBeTruthy();

    fireEvent.change(screen.getByDisplayValue("screen_recording_001"), {
      target: { value: "Ranked finish" },
    });
    fireEvent.change(screen.getAllByDisplayValue("Choose game")[0], {
      target: { value: "valorant" },
    });

    expect(screen.getByDisplayValue("Ranked finish")).toBeTruthy();
    expect(screen.getByDisplayValue("Valorant")).toBeTruthy();
  });

  it("applies bulk tag and collection metadata to selected rows", async () => {
    renderQueue();

    fireEvent.change(screen.getByLabelText("Choose video files"), {
      target: {
        files: [videoFile("Apex Legends/session_2026-05-04_2147.mp4")],
      },
    });

    await screen.findByDisplayValue("session_2026-05-04_2147");
    await waitFor(() => expect(screen.getByText("Best of 2026")).toBeTruthy());

    fireEvent.change(screen.getByPlaceholderText("ranked, clutch"), {
      target: { value: "ranked, clutch" },
    });
    fireEvent.click(screen.getByRole("button", { name: /apply/i }));
    fireEvent.change(screen.getByLabelText(/collection/i), {
      target: { value: "best-of" },
    });

    expect(screen.getByText("#ranked")).toBeTruthy();
    expect(screen.getByText("#clutch")).toBeTruthy();
    expect(screen.getAllByText("Best of 2026").length).toBeGreaterThan(1);
  });

  it("starts selected ready uploads with a three-row concurrency limit", async () => {
    renderQueue();

    fireEvent.change(screen.getByLabelText("Choose video files"), {
      target: {
        files: [
          videoFile("Apex Legends/clip-1.mp4"),
          videoFile("Apex Legends/clip-2.mp4"),
          videoFile("Apex Legends/clip-3.mp4"),
          videoFile("Apex Legends/clip-4.mp4"),
        ],
      },
    });

    await screen.findByDisplayValue("clip-1");
    fireEvent.click(screen.getByRole("button", { name: /add 4 to library/i }));

    await waitFor(() =>
      expect(clipUploadMocks.createTusClipUpload).toHaveBeenCalledTimes(3),
    );

    const firstCall = clipUploadMocks.createTusClipUpload.mock.calls[0]?.[0] as
      | { onSuccess: () => void }
      | undefined;
    firstCall?.onSuccess();

    await waitFor(() =>
      expect(clipUploadMocks.createTusClipUpload).toHaveBeenCalledTimes(4),
    );
    expect(screen.getByText("uploaded")).toBeTruthy();
  });

  it("marks same-game duplicate hashes before creating a second backend clip", async () => {
    clipUploadMocks.calculateClipUploadMd5.mockResolvedValue("same-hash");
    renderQueue();

    fireEvent.change(screen.getByLabelText("Choose video files"), {
      target: {
        files: [
          videoFile("Apex Legends/duplicate-a.mp4"),
          videoFile("Apex Legends/duplicate-b.mp4"),
        ],
      },
    });

    await screen.findByDisplayValue("duplicate-a");
    fireEvent.click(screen.getByRole("button", { name: /add 2 to library/i }));

    await waitFor(() =>
      expect(clipUploadMocks.createPreparedClipUpload).toHaveBeenCalledTimes(1),
    );
    expect(await screen.findByText("duplicate")).toBeTruthy();
  });

  it("files uploaded rows with tags, user collection, and a session collection", async () => {
    renderQueue();

    fireEvent.change(screen.getByLabelText("Choose video files"), {
      target: {
        files: [videoFile("Apex Legends/session-filed.mp4")],
      },
    });

    await screen.findByDisplayValue("session-filed");
    await waitFor(() => expect(screen.getByText("Best of 2026")).toBeTruthy());
    fireEvent.change(screen.getByPlaceholderText("ranked, clutch"), {
      target: { value: "ranked" },
    });
    fireEvent.click(screen.getByRole("button", { name: /apply/i }));
    fireEvent.change(screen.getByLabelText(/collection/i), {
      target: { value: "best-of" },
    });
    fireEvent.click(screen.getByRole("button", { name: /add 1 to library/i }));

    await waitFor(() =>
      expect(clipUploadMocks.createTusClipUpload).toHaveBeenCalledTimes(1),
    );
    const uploadCall = clipUploadMocks.createTusClipUpload.mock
      .calls[0]?.[0] as { onSuccess: () => void } | undefined;
    uploadCall?.onSuccess();

    await waitFor(() =>
      expect(serviceMocks.addTagToVideo).toHaveBeenCalledWith(
        "clip-hash-session-filed.mp4",
        "ranked",
      ),
    );
    expect(serviceMocks.addClipsToPlaylist).toHaveBeenCalledWith("best-of", {
      clipIds: ["clip-hash-session-filed.mp4"],
    });
    expect(serviceMocks.ensureGamingSessionPlaylist).toHaveBeenCalledWith(
      expect.objectContaining({
        categoryId: "apex",
        clipIds: ["clip-hash-session-filed.mp4"],
      }),
    );
    expect(await screen.findByText("Open collection")).toBeTruthy();
    expect(screen.queryByDisplayValue("session-filed")).toBeNull();
  });

  it("retries filing errors without re-uploading the clip", async () => {
    serviceMocks.ensureGamingSessionPlaylist
      .mockRejectedValueOnce(new Error("session unavailable"))
      .mockResolvedValueOnce({
        id: "session-playlist",
        clips: [],
        collaboratorCount: 0,
        collaborators: [],
        createdAt: new Date("2026-01-01T00:00:00Z"),
        creatorUserId: "user-1",
        description: null,
        name: "Apex Legends - 2026-05-04",
        updatedAt: new Date("2026-01-01T00:00:00Z"),
      });
    renderQueue();

    fireEvent.change(screen.getByLabelText("Choose video files"), {
      target: {
        files: [videoFile("Apex Legends/session-retry.mp4")],
      },
    });
    await screen.findByDisplayValue("session-retry");
    fireEvent.click(screen.getByRole("button", { name: /add 1 to library/i }));

    await waitFor(() =>
      expect(clipUploadMocks.createTusClipUpload).toHaveBeenCalledTimes(1),
    );
    const uploadCall = clipUploadMocks.createTusClipUpload.mock
      .calls[0]?.[0] as { onSuccess: () => void } | undefined;
    uploadCall?.onSuccess();

    expect(await screen.findByText("filing error")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText("Open collection")).toBeTruthy();
    expect(serviceMocks.ensureGamingSessionPlaylist).toHaveBeenCalledTimes(2);
    expect(clipUploadMocks.createTusClipUpload).toHaveBeenCalledTimes(1);
  });

  it("uses route fallback context only when source path game matching fails", async () => {
    renderQueue({
      fallbackCategoryId: "valorant",
      initialFiles: [
        videoFile("Apex Legends/path-wins.mp4"),
        videoFile("unknown-folder/fallback-used.mp4"),
      ],
    });

    await screen.findByDisplayValue("path-wins");
    expect(screen.getByDisplayValue("Apex Legends")).toBeTruthy();
    expect(screen.getByDisplayValue("fallback-used")).toBeTruthy();
    expect(screen.getByDisplayValue("Valorant")).toBeTruthy();
  });
});

function renderQueue(props: Parameters<typeof BulkUploadQueue>[0] = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <BulkUploadQueue {...props} />
    </QueryClientProvider>,
  );
}

function videoFile(path: string) {
  const file = new File(["clip"], path.split("/").at(-1)!, {
    type: "video/mp4",
    lastModified: new Date("2026-05-04T21:47:00").getTime(),
  });

  Object.defineProperty(file, "webkitRelativePath", {
    value: path,
  });

  return file;
}
