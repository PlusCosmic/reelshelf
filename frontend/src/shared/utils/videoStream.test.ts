// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import type { PlaybackSource } from "@/api-client";
import { attachStream, canPlayProgressive, splitCodecs } from "./videoStream";

const av1Type = 'video/mp4; codecs="av01.0.08M.08"';
const av1Source: PlaybackSource = {
  url: "https://media.example/video.mp4",
  type: av1Type,
  height: 1080,
};
const h264Type = 'video/mp4; codecs="avc1.640028"';
const h264Source: PlaybackSource = {
  url: "https://media.example/video-h264.mp4",
  type: h264Type,
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

function callbacks() {
  return { onFatal: vi.fn(), onUnsupported: vi.fn() };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  Reflect.deleteProperty(navigator, "mediaCapabilities");
});

describe("splitCodecs", () => {
  it("splits one video and one audio codec into their own types", () => {
    expect(splitCodecs('video/mp4; codecs="av01.0.08M.08,opus"')).toEqual({
      video: 'video/mp4; codecs="av01.0.08M.08"',
      audio: 'audio/mp4; codecs="opus"',
    });
    expect(splitCodecs('video/mp4; codecs="avc1.640028, mp4a.40.2"')).toEqual({
      video: 'video/mp4; codecs="avc1.640028"',
      audio: 'audio/mp4; codecs="mp4a.40.2"',
    });
  });

  it("leaves a type with one codec, none, or no clear split as it is", () => {
    for (const type of [
      av1Type,
      "video/mp4",
      'video/mp4; codecs="av01.0.08M.08,avc1.640028"',
    ]) {
      expect(splitCodecs(type)).toEqual({ video: type });
    }
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

  it("asks about the video and audio codecs separately, since MediaCapabilities takes one codec per stream", async () => {
    const decodingInfo = mockMediaCapabilities(() =>
      Promise.resolve({ supported: true }),
    );
    expect(
      await canPlayProgressive(video(), {
        ...av1Source,
        type: 'video/mp4; codecs="av01.0.08M.08,opus"',
      }),
    ).toBe(true);
    expect(decodingInfo).toHaveBeenCalledWith({
      type: "file",
      video: expect.objectContaining({ contentType: av1Type }) as unknown,
      audio: { contentType: 'audio/mp4; codecs="opus"' },
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
  it("plays a playable MP4 as the video's own src, and clears it on detach", async () => {
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
    // AV1 first: chosen where it decodes, skipped for the H.264 file behind it where it doesn't.
    const playsAv1 = video(av1Type, h264Type);
    attachStream(playsAv1, [av1Source, h264Source], callbacks());
    await vi.waitFor(() =>
      expect(playsAv1.getAttribute("src")).toBe(av1Source.url),
    );

    const noAv1 = video(h264Type);
    const handlers = callbacks();
    attachStream(noAv1, [av1Source, h264Source], handlers);
    await vi.waitFor(() =>
      expect(noAv1.getAttribute("src")).toBe(h264Source.url),
    );
    expect(handlers.onUnsupported).not.toHaveBeenCalled();
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
});
