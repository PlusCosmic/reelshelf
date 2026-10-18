// @vitest-environment jsdom
import { act, cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { PlaybackSource } from "@/api-client";
import type { StreamCallbacks } from "@/shared/utils/videoStream";
import { ClipVideoPlayer } from "./ClipVideoPlayer";

const stream = vi.hoisted(() => ({
  attached: [] as {
    sources: readonly PlaybackSource[];
    detached: boolean;
    callbacks: StreamCallbacks;
  }[],
}));

vi.mock("@/shared/utils/videoStream", () => ({
  attachStream: (
    _video: HTMLVideoElement,
    sources: readonly PlaybackSource[],
    callbacks: StreamCallbacks,
  ) => {
    const entry = { sources, detached: false, callbacks };
    stream.attached.push(entry);
    return () => {
      entry.detached = true;
    };
  },
}));

afterEach(() => {
  cleanup();
  stream.attached.length = 0;
});

const hls = (url: string): PlaybackSource[] => [
  { url, type: "application/vnd.apple.mpegurl" },
];
const first = "https://cdn.example/a/playlist.m3u8";
const second = "https://cdn.example/b/playlist.m3u8";

describe("ClipVideoPlayer", () => {
  it("detaches the old stream when the sources change and on unmount", () => {
    const view = render(<ClipVideoPlayer sources={hls(first)} title="A" />);
    view.rerender(<ClipVideoPlayer sources={hls(second)} title="B" />);
    expect(
      stream.attached.map((entry) => [entry.sources[0].url, entry.detached]),
    ).toEqual([
      [first, true],
      [second, false],
    ]);

    view.unmount();
    expect(stream.attached[1].detached).toBe(true);
  });

  it("keeps the stream when the same sources arrive in a new array", () => {
    const view = render(<ClipVideoPlayer sources={hls(first)} title="A" />);
    view.rerender(<ClipVideoPlayer sources={hls(first)} title="A" />);
    expect(stream.attached).toHaveLength(1);
    expect(stream.attached[0].detached).toBe(false);
  });

  it("goes black behind the video only once the current clip has a frame", () => {
    const view = render(<ClipVideoPlayer sources={hls(first)} title="A" />);
    const video = view.container.querySelector("video")!;
    expect(video.hasAttribute("data-loaded")).toBe(false);

    act(() => void video.dispatchEvent(new Event("loadeddata")));
    expect(video.hasAttribute("data-loaded")).toBe(true);

    view.rerender(<ClipVideoPlayer sources={hls(second)} title="B" />);
    expect(video.hasAttribute("data-loaded")).toBe(false);
  });

  it("says so when the stream can't be played, until the next clip", () => {
    const view = render(<ClipVideoPlayer sources={hls(first)} title="A" />);
    act(() => stream.attached[0].callbacks.onFatal());
    expect(view.getByRole("alert").textContent).toContain("couldn't be played");

    view.rerender(<ClipVideoPlayer sources={hls(second)} title="B" />);
    expect(view.queryByRole("alert")).toBeNull();
  });

  it("says the clip can't play on this device when no source can, until the next clip", () => {
    const view = render(<ClipVideoPlayer sources={hls(first)} title="A" />);
    act(() => stream.attached[0].callbacks.onUnsupported());
    expect(view.getByRole("alert").textContent).toBe(
      "This clip can't play on this device.",
    );

    view.rerender(<ClipVideoPlayer sources={hls(second)} title="B" />);
    expect(view.queryByRole("alert")).toBeNull();
  });
});
