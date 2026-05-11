// @vitest-environment jsdom
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
  useBlocker: vi.fn(),
}));

vi.mock("@/hooks/queries", () => ({
  useCategories: () => ({
    data: categories,
    isLoading: false,
  }),
}));

vi.mock("@/shared/services/playlists", () => ({
  fetchPlaylists: vi.fn(async () => playlists),
}));

describe("BulkUploadQueue", () => {
  beforeEach(() => {
    vi.clearAllMocks();
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
});

function renderQueue() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <BulkUploadQueue />
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
