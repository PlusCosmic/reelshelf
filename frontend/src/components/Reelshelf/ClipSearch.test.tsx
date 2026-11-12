// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ClipSearchRequest, ClipSearchResponse } from "@/api-client";
import { ClipSearchForm, ClipSearchResults } from "./ClipSearch";
import type { ClipSearchState } from "./clipSearchModel";

const mocks = vi.hoisted(() => ({
  searchClips: vi.fn(),
}));

vi.mock(import("@/shared/services/apiClients"), async (importOriginal) => ({
  ...(await importOriginal()),
  createClipSearchApi: () => ({ searchClips: mocks.searchClips }) as never,
}));

afterEach(() => {
  cleanup();
  mocks.searchClips.mockReset();
});

const read: ClipSearchResponse = {
  query: "funny cod clips with sam",
  interpretation: {
    startDate: null,
    endDate: null,
    games: [{ id: "cod", name: "Call of Duty" }],
    moodTags: ["funny"],
    people: ["Sam"],
    phrase: null,
  },
  clips: [],
};

function Results({ initial }: { initial: ClipSearchState }) {
  const [state, setState] = useState(initial);
  return (
    <ClipSearchResults
      state={state}
      categories={[]}
      onChange={setState}
      onClear={() => {}}
    />
  );
}

function renderResults(initial: ClipSearchState) {
  const queryClient = new QueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <Results initial={initial} />
    </QueryClientProvider>,
  );
}

const sent = (call: number) =>
  (
    mocks.searchClips.mock.calls[call][0] as {
      clipSearchRequest: ClipSearchRequest;
    }
  ).clipSearchRequest;

describe("ClipSearchResults", () => {
  it("shows how the search was read, and takes a filter away without reading it again", async () => {
    mocks.searchClips.mockResolvedValueOnce(read).mockResolvedValueOnce({
      ...read,
      interpretation: { ...read.interpretation, moodTags: [] },
    });

    renderResults({ query: read.query, filters: null });

    expect(await screen.findByText("Call of Duty")).toBeTruthy();
    expect(screen.getByText("funny")).toBeTruthy();
    expect(screen.getByText("With Sam")).toBeTruthy();
    expect(screen.getByText("No clips fit")).toBeTruthy();
    expect(sent(0).filters).toBeNull();
    expect(sent(0).query).toBe(read.query);
    // The answer was filed under the filters it was read into, so moving onto them asks for nothing.
    expect(mocks.searchClips).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole("button", { name: "Remove funny" }));

    expect(await screen.findByText("With Sam")).toBeTruthy();
    await vi.waitFor(() => expect(mocks.searchClips).toHaveBeenCalledTimes(2));
    expect(sent(1).filters).toMatchObject({
      gameCategoryIds: ["cod"],
      moodTags: [],
      people: ["Sam"],
    });
    await vi.waitFor(() => expect(screen.queryByText("funny")).toBeNull());
  });

  it("says so when every filter is gone, without searching", () => {
    renderResults({
      query: "anything",
      filters: {
        startDate: null,
        endDate: null,
        gameCategoryIds: [],
        moodTags: [],
        people: [],
        phrase: null,
      },
    });

    expect(
      screen.getByText(
        "Every filter is gone. Describe the clip again to search.",
      ),
    ).toBeTruthy();
    expect(mocks.searchClips).not.toHaveBeenCalled();
  });
});

describe("ClipSearchForm", () => {
  it("searches the trimmed description, and refuses one too long to read", () => {
    const onSearch = vi.fn();
    render(<ClipSearchForm initialQuery="" onSearch={onSearch} />);
    const input = screen.getByLabelText("Describe a clip");

    fireEvent.change(input, { target: { value: "  clutch in apex  " } });
    fireEvent.click(screen.getByRole("button", { name: "Find clips" }));
    expect(onSearch).toHaveBeenCalledWith("clutch in apex");

    fireEvent.change(input, { target: { value: "a".repeat(301) } });
    expect(
      (screen.getByRole("button", { name: "Find clips" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    expect(
      screen.getByText("Keep it shorter: a sentence is plenty."),
    ).toBeTruthy();
  });
});
