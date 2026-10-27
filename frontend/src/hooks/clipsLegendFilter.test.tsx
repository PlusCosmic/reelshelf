// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { useClipsInfinite } from "./clips.queries";

const mocks = vi.hoisted(() => ({
  fetchClips: vi.fn(() =>
    Promise.resolve({ clips: [], totalClips: 0, totalPages: 0 }),
  ),
}));

vi.mock("@/shared/services/clips", () => ({
  fetchClips: mocks.fetchClips,
}));

describe("useClipsInfinite legend filter", () => {
  it("asks the server for the legend's clips, under a key of their own", async () => {
    const queryClient = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    const filters = { categoryId: "apex", legend: "Wraith" };

    renderHook(() => useClipsInfinite(filters), { wrapper });

    await waitFor(() => expect(mocks.fetchClips).toHaveBeenCalled());
    expect(mocks.fetchClips).toHaveBeenCalledWith(
      expect.objectContaining(filters),
    );
    const key = ["clips", "list", filters];
    expect(queryClient.getQueryCache().find({ queryKey: key })).toBeDefined();
  });
});
