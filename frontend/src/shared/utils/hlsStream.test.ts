// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { attachHlsStream, hlsPlayback } from "./hlsStream";

const hls = vi.hoisted(() => {
  const instances: {
    source?: string;
    media?: HTMLVideoElement;
    destroyed: boolean;
    onError?: (event: string, data: { fatal: boolean }) => void;
  }[] = [];
  class FakeHls {
    static isSupported = () => true;
    static Events = { ERROR: "hlsError" };
    state: (typeof instances)[number] = { destroyed: false };
    constructor() {
      instances.push(this.state);
    }
    on(
      _event: string,
      handler: (event: string, data: { fatal: boolean }) => void,
    ) {
      this.state.onError = handler;
    }
    loadSource(source: string) {
      this.state.source = source;
    }
    attachMedia(media: HTMLVideoElement) {
      this.state.media = media;
    }
    destroy() {
      this.state.destroyed = true;
    }
  }
  return { instances, FakeHls };
});

vi.mock("hls.js/light", () => ({ default: hls.FakeHls }));

const playlist = "https://cdn.example/video/playlist.m3u8";

function video(canPlayHls: boolean) {
  const element = document.createElement("video");
  vi.spyOn(element, "canPlayType").mockReturnValue(canPlayHls ? "maybe" : "");
  vi.spyOn(element, "load").mockImplementation(() => undefined);
  return element;
}

afterEach(() => {
  hls.instances.length = 0;
});

describe("hlsPlayback", () => {
  it("uses hls.js wherever MediaSource exists, even where the browser plays HLS itself", () => {
    expect(hlsPlayback(video(false), true)).toBe("hls.js");
    expect(hlsPlayback(video(true), true)).toBe("hls.js");
  });

  it("uses the browser's own player without MediaSource (iPhone Safari)", () => {
    expect(hlsPlayback(video(true), false)).toBe("native");
  });

  it("leaves a browser with neither to hls.js, which reports it can't play", () => {
    expect(hlsPlayback(video(false), false)).toBe("hls.js");
  });
});

describe("attachHlsStream", () => {
  it("plays natively by setting the source, and clears it on detach", () => {
    // jsdom has no MediaSource, so a browser that plays HLS itself takes the native path.
    const element = video(true);
    const onFatal = vi.fn();
    const detach = attachHlsStream(element, playlist, onFatal);
    expect(element.getAttribute("src")).toBe(playlist);

    element.dispatchEvent(new Event("error"));
    expect(onFatal).toHaveBeenCalledTimes(1);

    detach();
    expect(element.hasAttribute("src")).toBe(false);
    element.dispatchEvent(new Event("error"));
    expect(onFatal).toHaveBeenCalledTimes(1);
    expect(hls.instances).toHaveLength(0);
  });

  it("loads hls.js, attaches it, and destroys it on detach", async () => {
    const element = video(false);
    const onFatal = vi.fn();
    const detach = attachHlsStream(element, playlist, onFatal);

    await vi.waitFor(() => expect(hls.instances).toHaveLength(1));
    const [instance] = hls.instances;
    expect(instance.source).toBe(playlist);
    expect(instance.media).toBe(element);

    instance.onError?.("hlsError", { fatal: false });
    expect(onFatal).not.toHaveBeenCalled();
    instance.onError?.("hlsError", { fatal: true });
    expect(onFatal).toHaveBeenCalledTimes(1);

    detach();
    expect(instance.destroyed).toBe(true);
  });

  it("never starts hls.js when detached before it has loaded", async () => {
    const detach = attachHlsStream(video(false), playlist, vi.fn());
    detach();
    await import("hls.js/light");
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(hls.instances).toHaveLength(0);
  });
});
