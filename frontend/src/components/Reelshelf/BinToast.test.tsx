// @vitest-environment jsdom
import type { ReactNode } from "react";
import { act, cleanup, fireEvent, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BinProvider, useBin } from "./BinToast";

const mocks = vi.hoisted(() => ({
  binClips: vi.fn(async (target: { clipIds: string[] }) => ({
    clipIds: target.clipIds,
  })),
  restore: vi.fn(async (clipIds: string[]) => ({ clipIds })),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
}));

vi.mock("@/hooks/bin.queries", () => ({
  useBinClips: () => ({ mutateAsync: mocks.binClips, isPending: false }),
  useRestoreClips: () => ({ mutateAsync: mocks.restore, isPending: false }),
}));

let actions: ReturnType<typeof useBin> | null = null;
function Capture() {
  actions = useBin();
  return null;
}

function renderProvider() {
  return render(
    <BinProvider>
      <Capture />
    </BinProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  vi.useRealTimers();
  actions = null;
});

describe("BinProvider", () => {
  it("keeps a newer binned note when an older undo finishes", async () => {
    let finishRestore: (value: { clipIds: string[] }) => void = () => {};
    mocks.restore.mockImplementationOnce(
      () => new Promise((resolve) => (finishRestore = resolve)),
    );
    const view = renderProvider();

    await act(() => actions!.bin({ clipIds: ["a"] }));
    fireEvent.click(view.getByText("Undo"));
    await act(() => actions!.bin({ clipIds: ["b", "c"] }));
    await act(async () => finishRestore({ clipIds: ["a"] }));

    expect(view.getByText("2 clips moved to the bin.")).toBeTruthy();
    expect(view.queryByText("Clip put back.")).toBeNull();
  });

  it("says clips were put back when nothing newer came along", async () => {
    const view = renderProvider();

    await act(() => actions!.bin({ clipIds: ["a"] }));
    await act(async () => fireEvent.click(view.getByText("Undo")));

    expect(await view.findByText("Clip put back.")).toBeTruthy();
  });

  it("lets go of a note closed while it had focus, so the next one still goes", async () => {
    vi.useFakeTimers();
    const view = renderProvider();

    await act(() => actions!.bin({ clipIds: ["a"] }));
    const dismiss = view.getByLabelText("Dismiss");
    fireEvent.focus(dismiss);
    fireEvent.click(dismiss);
    expect(view.queryByText("Clip moved to the bin.")).toBeNull();

    await act(() => actions!.bin({ clipIds: ["b"] }));
    expect(view.getByText("Clip moved to the bin.")).toBeTruthy();
    act(() => vi.advanceTimersByTime(10_000));
    expect(view.queryByText("Clip moved to the bin.")).toBeNull();
  });
});
