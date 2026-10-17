import { describe, expect, it, vi } from "vitest";
import type { CreateClipResponse } from "@/api-client";
import { createPreparedClipUpload, createTusClipUpload } from "./clipUpload";

vi.mock("tus-js-client", () => ({
  Upload: class {
    constructor(
      public file: unknown,
      public options: Record<string, unknown>,
    ) {}
  },
}));

const prepared: CreateClipResponse = {
  clipId: "clip-id",
  videoId: "video-id",
  upload: {
    protocol: "tus",
    endpoint: "https://uploads.example/tus",
    headers: { "X-Upload-Token": "token", "X-Video": "video-id" },
    metadata: { title: "Triple kill", folder: "apex" },
    expiresAt: new Date("2026-10-17T13:00:00Z"),
  },
};

const noop = () => undefined;

function startUpload(response: CreateClipResponse, file: File) {
  return createTusClipUpload({
    file,
    response,
    onProgress: noop,
    onSuccess: noop,
    onError: noop,
  }) as unknown as { options: Record<string, unknown> };
}

describe("createPreparedClipUpload", () => {
  it("returns the prepared clip and its upload target", async () => {
    const createVideo = vi.fn().mockResolvedValue(prepared);

    const response = await createPreparedClipUpload({
      categoryId: "apex-id",
      createVideo,
      fileSize: 10,
      md5Hash: "hash",
      title: "Triple kill",
    });

    expect(response).toBe(prepared);
    expect(createVideo).toHaveBeenCalledWith({
      categoryId: "apex-id",
      title: "Triple kill",
      fileSize: 10,
      md5Hash: "hash",
      createdAt: undefined,
    });
  });

  it("fails when the API prepared no upload", async () => {
    await expect(
      createPreparedClipUpload({
        categoryId: "apex-id",
        createVideo: vi.fn().mockResolvedValue(null),
        fileSize: 10,
        md5Hash: "hash",
        title: "Triple kill",
      }),
    ).rejects.toThrow("The upload could not be prepared.");
  });
});

describe("createTusClipUpload", () => {
  it("sends the file where the API said, with its headers and metadata", () => {
    const file = new File(["bytes"], "clip.mp4", { type: "video/webm" });

    const { options } = startUpload(prepared, file);

    expect(options.endpoint).toBe("https://uploads.example/tus");
    expect(options.headers).toEqual({
      "X-Upload-Token": "token",
      "X-Video": "video-id",
    });
    expect(options.metadata).toEqual({
      title: "Triple kill",
      folder: "apex",
      filename: "clip.mp4",
      filetype: "video/webm",
    });
    expect(options.retryDelays).toEqual([0, 1000, 3000, 5000, 10000]);
  });

  it("describes a file with no type as mp4", () => {
    const { options } = startUpload(prepared, new File(["bytes"], "clip"));

    expect(options.metadata).toMatchObject({ filetype: "video/mp4" });
  });

  it("refuses an upload protocol it does not speak", () => {
    const response = {
      ...prepared,
      upload: { ...prepared.upload, protocol: "s3-multipart" },
    };

    expect(() =>
      startUpload(response, new File(["bytes"], "clip.mp4")),
    ).toThrow('unsupported upload protocol "s3-multipart"');
  });
});
