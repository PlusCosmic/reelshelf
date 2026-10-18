// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ClipVideoStatus, type PlaybackSource } from "@/api-client";
import { ClipVideoStage, clipProcessingMessage } from "./ClipVideoStage";

const stream = vi.hoisted(() => ({
  attached: [] as readonly PlaybackSource[][],
}));

vi.mock("@/shared/utils/videoStream", () => ({
  attachStream: (_video: HTMLVideoElement, sources: PlaybackSource[]) => {
    (stream.attached as PlaybackSource[][]).push(sources);
    return () => undefined;
  },
}));

afterEach(() => {
  cleanup();
  (stream.attached as PlaybackSource[][]).length = 0;
});

const mp4: PlaybackSource[] = [
  {
    url: "https://media.example/clips/a/v1/av1.mp4",
    type: 'video/mp4; codecs="av01.0.08M.08,opus"',
  },
];
const thumbnail = "https://media.example/clips/a/v1/thumbnail.jpg";

describe("clipProcessingMessage", () => {
  it("treats a clip waiting for or holding its upload as processing", () => {
    for (const status of [
      ClipVideoStatus.AwaitingUpload,
      ClipVideoStatus.Uploaded,
      ClipVideoStatus.Processing,
    ]) {
      expect(clipProcessingMessage(status, 0)).toMatchObject({
        title: "Processing",
        failed: false,
        progress: null,
      });
    }
    expect(clipProcessingMessage(ClipVideoStatus.Processing, 63)).toMatchObject(
      { title: "Processing 63%", progress: 63 },
    );
  });

  it("says plainly when the encode or the upload failed", () => {
    expect(clipProcessingMessage(ClipVideoStatus.EncodeFailed, 80)).toEqual({
      title: "This clip couldn’t be processed.",
      detail: "Its video couldn’t be encoded. Try uploading it again.",
      failed: true,
      progress: null,
    });
    expect(
      clipProcessingMessage(ClipVideoStatus.UploadFailed, 0),
    ).toMatchObject({
      title: "This clip’s upload didn’t finish.",
      failed: true,
    });
  });
});

describe("ClipVideoStage", () => {
  it("plays a ready clip", () => {
    const view = render(
      <ClipVideoStage
        status={ClipVideoStatus.Ready}
        sources={mp4}
        poster={thumbnail}
        title="A"
      />,
    );
    const video = view.container.querySelector("video");
    expect(video?.getAttribute("poster")).toBe(thumbnail);
    expect(stream.attached).toEqual([mp4]);
    expect(view.queryByRole("status")).toBeNull();
  });

  it("shows the processing state, never the player, until the clip is ready", () => {
    const view = render(
      <ClipVideoStage
        status={ClipVideoStatus.Processing}
        encodeProgress={25}
        sources={[]}
        poster={null}
        title="A"
      />,
    );
    expect(view.container.querySelector("video")).toBeNull();
    expect(stream.attached).toEqual([]);
    expect(view.getByRole("status").textContent).toContain("Processing 25%");
    expect(view.container.querySelector("img")).toBeNull();
    expect(
      (view.container.querySelector(".rs-thumb-progress span") as HTMLElement)
        .style.width,
    ).toBe("25%");

    // The thumbnail arrives while it encodes.
    view.rerender(
      <ClipVideoStage
        status={ClipVideoStatus.Processing}
        encodeProgress={60}
        sources={[]}
        poster={thumbnail}
        title="A"
      />,
    );
    expect(view.container.querySelector("img")?.getAttribute("src")).toBe(
      thumbnail,
    );
    expect(view.container.querySelector("video")).toBeNull();
  });

  it("shows a failure as an alert, with the thumbnail it has", () => {
    const view = render(
      <ClipVideoStage
        status={ClipVideoStatus.EncodeFailed}
        sources={[]}
        poster={thumbnail}
        title="A"
      />,
    );
    expect(view.getByRole("alert").textContent).toContain(
      "This clip couldn’t be processed.",
    );
    expect(view.container.querySelector(".rs-spinner")).toBeNull();
    expect(view.container.querySelector("video")).toBeNull();
    expect(view.queryByText("This clip can't play on this device.")).toBeNull();
  });
});
