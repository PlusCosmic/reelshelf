// @vitest-environment jsdom
import { act, cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ClipVideoPlayer } from "./ClipVideoPlayer";

const stream = vi.hoisted(() => ({
  attached: [] as { source: string; detached: boolean; fail: () => void }[],
}));

vi.mock("@/shared/utils/hlsStream", () => ({
  attachHlsStream: (
    _video: HTMLVideoElement,
    source: string,
    onFatal: () => void,
  ) => {
    const entry = { source, detached: false, fail: onFatal };
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

const first = "https://cdn.example/a/playlist.m3u8";
const second = "https://cdn.example/b/playlist.m3u8";

describe("ClipVideoPlayer", () => {
  it("detaches the old stream when the source changes and on unmount", () => {
    const view = render(<ClipVideoPlayer src={first} title="A" />);
    view.rerender(<ClipVideoPlayer src={second} title="B" />);
    expect(
      stream.attached.map((entry) => [entry.source, entry.detached]),
    ).toEqual([
      [first, true],
      [second, false],
    ]);

    view.unmount();
    expect(stream.attached[1].detached).toBe(true);
  });

  it("goes black behind the video only once the current clip has a frame", () => {
    const view = render(<ClipVideoPlayer src={first} title="A" />);
    const video = view.container.querySelector("video")!;
    expect(video.hasAttribute("data-loaded")).toBe(false);

    act(() => void video.dispatchEvent(new Event("loadeddata")));
    expect(video.hasAttribute("data-loaded")).toBe(true);

    view.rerender(<ClipVideoPlayer src={second} title="B" />);
    expect(video.hasAttribute("data-loaded")).toBe(false);
  });

  it("says so when the stream can't be played, until the next clip", () => {
    const view = render(<ClipVideoPlayer src={first} title="A" />);
    act(() => stream.attached[0].fail());
    expect(view.getByRole("alert").textContent).toContain("couldn't be played");

    view.rerender(<ClipVideoPlayer src={second} title="B" />);
    expect(view.queryByRole("alert")).toBeNull();
  });
});
