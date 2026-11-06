import type { PlaybackSource } from "@/api-client";

/**
 * The size a progressive source is checked at when the API gives no height: the 1080p the pipeline encodes, at
 * the 60 fps game clips are usually recorded at, so a device that says yes can play the real thing.
 */
const assumedHeight = 1080;
const assumedFramerate = 60;
const assumedBitrate = 8_000_000;

/**
 * Whether the browser decodes a progressive file (MP4), asked before anything loads: MediaCapabilities where it
 * exists, which knows about hardware decoders, else `canPlayType` on the type with its codecs.
 */
export async function canPlayProgressive(
  video: HTMLVideoElement,
  source: PlaybackSource,
): Promise<boolean> {
  const capabilities =
    typeof navigator === "undefined" ? undefined : navigator.mediaCapabilities;
  if (typeof capabilities?.decodingInfo === "function") {
    const height = source.height ?? assumedHeight;
    try {
      const { video: videoType, audio: audioType } = splitCodecs(source.type);
      const info = await capabilities.decodingInfo({
        type: "file",
        video: {
          contentType: videoType,
          width: Math.round((height * 16) / 9),
          height,
          bitrate: assumedBitrate,
          framerate: assumedFramerate,
        },
        ...(audioType ? { audio: { contentType: audioType } } : {}),
      });
      return info.supported;
    } catch {
      // A type the browser won't even parse here (no codecs, say); `canPlayType` still gives an answer.
    }
  }
  return video.canPlayType(source.type) !== "";
}

const audioCodecPrefixes = ["opus", "mp4a", "flac", "vorbis", "ac-3", "ec-3"];

/**
 * MediaCapabilities takes one codec per stream: a video type listing the audio codec too (the pipeline's
 * `video/mp4; codecs="av01.0.08M.08,opus"`) is answered "unsupported" by Chrome rather than rejected. So the
 * video codec goes in the video type and the audio codec in its own audio type. A type it can't split this way
 * is passed on whole, as before.
 */
export function splitCodecs(type: string): { video: string; audio?: string } {
  const [container] = type.split(";");
  const codecs = /codecs\s*=\s*"?([^";]*)"?/i
    .exec(type)?.[1]
    ?.split(",")
    .map((codec) => codec.trim())
    .filter(Boolean);
  if (!codecs || codecs.length < 2) return { video: type };

  const isAudio = (codec: string) =>
    audioCodecPrefixes.some((prefix) => codec.toLowerCase().startsWith(prefix));
  const videoCodecs = codecs.filter((codec) => !isAudio(codec));
  const audioCodecs = codecs.filter(isAudio);
  if (videoCodecs.length !== 1 || audioCodecs.length !== 1)
    return { video: type };

  const base = container.trim();
  return {
    video: `${base}; codecs="${videoCodecs[0]}"`,
    audio: `${base.replace(/^video\//i, "audio/")}; codecs="${audioCodecs[0]}"`,
  };
}

export type StreamCallbacks = {
  /** The chosen source failed while loading or playing. */
  onFatal: () => void;
  /** No source can play on this device, so nothing was attached. */
  onUnsupported: () => void;
};

/**
 * Attaches the first of a clip's sources this device can play to a `<video>`, in the order given, and returns
 * the detach function, which stops loading and frees whatever was attached. A progressive file (MP4) is the
 * video's own `src`.
 *
 * Checking a source is asynchronous, so detaching before the answer arrives leaves the video untouched and calls
 * neither callback.
 */
export function attachStream(
  video: HTMLVideoElement,
  sources: readonly PlaybackSource[],
  { onFatal, onUnsupported }: StreamCallbacks,
): () => void {
  let detached = false;
  let detachSource: (() => void) | undefined;

  const tryFrom = (index: number): void => {
    if (detached) return;
    const source = sources.at(index);
    if (!source) {
      onUnsupported();
      return;
    }

    void canPlayProgressive(video, source).then((playable) => {
      if (detached) return;
      if (playable) {
        detachSource = attachSrc(video, source.url, onFatal);
      } else {
        tryFrom(index + 1);
      }
    });
  };

  tryFrom(0);

  return () => {
    detached = true;
    detachSource?.();
    detachSource = undefined;
  };
}

/** Plays a progressive file as the video's own `src`. */
function attachSrc(
  video: HTMLVideoElement,
  url: string,
  onFatal: () => void,
): () => void {
  video.addEventListener("error", onFatal);
  video.src = url;
  return () => {
    video.removeEventListener("error", onFatal);
    video.removeAttribute("src");
    video.load();
  };
}
