// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import type { PlaybackSource } from "@/api-client";
import {
  attachStream,
  canPlayProgressive,
  hlsPlayback,
  hlsType,
} from "./videoStream";

const hls = vi.hoisted(() => {
  const instances: {
    source?: string;
    media?: HTMLVideoElement;
    destroyed: boolean;
    onError?: (event: string, data: { fatal: boolean }) => void;
  }[] = [];
  const support = { supported: true };
  class FakeHls {
    static isSupported = () => support.supported;
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
  return { instances, support, FakeHls };
});

vi.mock("hls.js/light", () => ({ default: hls.FakeHls }));

const playlist = "https://cdn.example/video/playlist.m3u8";
const av1Type = 'video/mp4; codecs="av01.0.08M.08"';
const hlsSource: PlaybackSource = { url: playlist, type: hlsType };
const av1Source: PlaybackSource = {
  url: "https://media.example/video.mp4",
  type: av1Type,
  height: 1080,
};

/** A `<video>` whose `canPlayType` answers yes to the given types. */
function video(...playable: string[]) {
  const element = document.createElement("video");
  vi.spyOn(element, "canPlayType").mockImplementation((type) =>
    playable.includes(type) ? "maybe" : "",
  );
  vi.spyOn(element, "load").mockImplementation(() => undefined);
  return element;
}

/** Stands in for `navigator.mediaCapabilities`; jsdom has none. */
function mockMediaCapabilities(
  decodingInfo: (
    config: MediaDecodingConfiguration,
  ) => Promise<{ supported: boolean }>,
) {
  const spy = vi.fn(decodingInfo);
  Object.defineProperty(navigator, "mediaCapabilities", {
    configurable: true,
    value: { decodingInfo: spy },
  });
  return spy;
}

function withMediaSource() {
  Object.defineProperty(window, "MediaSource", {
    configurable: true,
    writable: true,
    value: class {},
  });
}

function callbacks() {
  return { onFatal: vi.fn(), onUnsupported: vi.fn() };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  hls.instances.length = 0;
  hls.support.supported = true;
  Reflect.deleteProperty(navigator, "mediaCapabilities");
  Reflect.deleteProperty(window, "MediaSource");
});

describe("hlsPlayback", () => {
  it("uses hls.js wherever MediaSource exists, even where the browser plays HLS itself", () => {
    expect(hlsPlayback(video(), true)).toBe("hls.js");
    expect(hlsPlayback(video(hlsType), true)).toBe("hls.js");
  });

  it("uses the browser's own player without MediaSource (iPhone Safari)", () => {
    expect(hlsPlayback(video(hlsType), false)).toBe("native");
  });

  it("can't play HLS in a browser with neither", () => {
    expect(hlsPlayback(video(), false)).toBe("unsupported");
  });
});

describe("canPlayProgressive", () => {
  it("asks MediaCapabilities about the file at the source's size", async () => {
    const decodingInfo = mockMediaCapabilities(() =>
      Promise.resolve({ supported: true }),
    );
    // canPlayType would say no; MediaCapabilities wins where it exists.
    expect(
      await canPlayProgressive(video(), { ...av1Source, height: 720 }),
    ).toBe(true);
    expect(decodingInfo).toHaveBeenCalledWith({
      type: "file",
      video: expect.objectContaining({
        contentType: av1Type,
        width: 1280,
        height: 720,
      }) as unknown,
    });
  });

  it("falls back to canPlayType without MediaCapabilities, or when it rejects the type", async () => {
    expect(await canPlayProgressive(video(av1Type), av1Source)).toBe(true);
    expect(await canPlayProgressive(video(), av1Source)).toBe(false);

    mockMediaCapabilities(() => Promise.reject(new TypeError("bad type")));
    expect(await canPlayProgressive(video(av1Type), av1Source)).toBe(true);
  });
});

describe("attachStream", () => {
  it("plays a playable MP4 as the video's own src, with no hls.js, and clears it on detach", async () => {
    mockMediaCapabilities(() => Promise.resolve({ supported: true }));
    const element = video();
    const handlers = callbacks();
    const detach = attachStream(element, [av1Source], handlers);

    await vi.waitFor(() =>
      expect(element.getAttribute("src")).toBe(av1Source.url),
    );
    element.dispatchEvent(new Event("error"));
    expect(handlers.onFatal).toHaveBeenCalledTimes(1);

    detach();
    expect(element.hasAttribute("src")).toBe(false);
    element.dispatchEvent(new Event("error"));
    expect(handlers.onFatal).toHaveBeenCalledTimes(1);
    expect(handlers.onUnsupported).not.toHaveBeenCalled();
    expect(hls.instances).toHaveLength(0);
  });

  it("reports an MP4 this device can't decode as unsupported, attaching nothing", async () => {
    mockMediaCapabilities(() => Promise.resolve({ supported: false }));
    const element = video();
    const handlers = callbacks();
    attachStream(element, [av1Source], handlers);

    await vi.waitFor(() => expect(handlers.onUnsupported).toHaveBeenCalled());
    expect(element.hasAttribute("src")).toBe(false);
    expect(handlers.onFatal).not.toHaveBeenCalled();
  });

  it("reports no sources at all as unsupported", () => {
    const handlers = callbacks();
    attachStream(video(), [], handlers);
    expect(handlers.onUnsupported).toHaveBeenCalledTimes(1);
  });

  it("attaches the first playable source in order", async () => {
    // AV1 first: chosen where it decodes, skipped for the HLS behind it where it doesn't.
    mockMediaCapabilities(() => Promise.resolve({ supported: true }));
    const playsAv1 = video(hlsType);
    attachStream(playsAv1, [av1Source, hlsSource], callbacks());
    await vi.waitFor(() =>
      expect(playsAv1.getAttribute("src")).toBe(av1Source.url),
    );

    mockMediaCapabilities(() => Promise.resolve({ supported: false }));
    const noAv1 = video(hlsType);
    const handlers = callbacks();
    attachStream(noAv1, [av1Source, hlsSource], handlers);
    await vi.waitFor(() => expect(noAv1.getAttribute("src")).toBe(playlist));
    expect(handlers.onUnsupported).not.toHaveBeenCalled();

    // HLS first wins without asking about the MP4 at all.
    const decodingInfo = mockMediaCapabilities(() =>
      Promise.resolve({ supported: true }),
    );
    const hlsFirst = video(hlsType);
    attachStream(hlsFirst, [hlsSource, av1Source], callbacks());
    expect(hlsFirst.getAttribute("src")).toBe(playlist);
    expect(decodingInfo).not.toHaveBeenCalled();
  });

  it("leaves the video alone when detached before the MP4 check answers", async () => {
    let answer: (info: { supported: boolean }) => void = () => undefined;
    mockMediaCapabilities(
      () =>
        new Promise((resolve) => {
          answer = resolve;
        }),
    );
    const element = video();
    const handlers = callbacks();
    const detach = attachStream(element, [av1Source], handlers);
    detach();
    answer({ supported: true });
    await settle();

    expect(element.hasAttribute("src")).toBe(false);
    expect(handlers.onFatal).not.toHaveBeenCalled();
    expect(handlers.onUnsupported).not.toHaveBeenCalled();
  });

  it("plays HLS natively by setting the source at once, and clears it on detach", () => {
    // jsdom has no MediaSource, so a browser that plays HLS itself takes the native path.
    const element = video(hlsType);
    const handlers = callbacks();
    const detach = attachStream(element, [hlsSource], handlers);
    expect(element.getAttribute("src")).toBe(playlist);

    element.dispatchEvent(new Event("error"));
    expect(handlers.onFatal).toHaveBeenCalledTimes(1);

    detach();
    expect(element.hasAttribute("src")).toBe(false);
    element.dispatchEvent(new Event("error"));
    expect(handlers.onFatal).toHaveBeenCalledTimes(1);
    expect(hls.instances).toHaveLength(0);
  });

  it("reports HLS as unsupported where neither hls.js nor the browser can play it", () => {
    const handlers = callbacks();
    attachStream(video(), [hlsSource], handlers);
    expect(handlers.onUnsupported).toHaveBeenCalledTimes(1);
    expect(hls.instances).toHaveLength(0);
  });

  it("loads hls.js where MediaSource exists, attaches it, and destroys it on detach", async () => {
    withMediaSource();
    const element = video(hlsType);
    const handlers = callbacks();
    const detach = attachStream(element, [hlsSource], handlers);

    await vi.waitFor(() => expect(hls.instances).toHaveLength(1));
    const [instance] = hls.instances;
    expect(instance.source).toBe(playlist);
    expect(instance.media).toBe(element);
    expect(element.hasAttribute("src")).toBe(false);

    instance.onError?.("hlsError", { fatal: false });
    expect(handlers.onFatal).not.toHaveBeenCalled();
    instance.onError?.("hlsError", { fatal: true });
    expect(handlers.onFatal).toHaveBeenCalledTimes(1);

    detach();
    expect(instance.destroyed).toBe(true);
  });

  it("moves on when hls.js finds it can't play here", async () => {
    withMediaSource();
    hls.support.supported = false;
    const handlers = callbacks();
    attachStream(video(), [hlsSource], handlers);

    await vi.waitFor(() => expect(handlers.onUnsupported).toHaveBeenCalled());
    expect(hls.instances).toHaveLength(0);
  });

  it("never starts hls.js when detached before it has loaded", async () => {
    withMediaSource();
    const handlers = callbacks();
    const detach = attachStream(video(), [hlsSource], handlers);
    detach();
    await import("hls.js/light");
    await settle();
    expect(hls.instances).toHaveLength(0);
    expect(handlers.onUnsupported).not.toHaveBeenCalled();
  });
});
