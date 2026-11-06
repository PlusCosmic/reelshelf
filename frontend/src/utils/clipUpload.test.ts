import { describe, expect, it, vi } from "vitest";
import type { CreateClipResponse } from "@/api-client";
import { createPreparedClipUpload, createS3ClipUpload } from "./clipUpload";
import { uploadCopy } from "@/copy/upload";
import { S3MultipartUpload } from "./s3MultipartUpload";

const prepared: CreateClipResponse = {
  clipId: "clip-id",
  videoId: "video-id",
  upload: {
    protocol: "carrier-pigeon",
    endpoint: "/api/clips/videos/clip-id/upload",
    headers: {},
    metadata: {},
    expiresAt: new Date("2026-10-17T13:00:00Z"),
  },
};

const noop = () => undefined;

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
    ).rejects.toThrow(uploadCopy.errors.notStarted);
  });
});

describe("createS3ClipUpload", () => {
  it("starts nothing for a target that isn't a multipart upload", () => {
    expect(() =>
      createS3ClipUpload({
        file: new File(["bytes"], "clip.mp4"),
        response: prepared,
        resumeKey: "key",
        onProgress: noop,
        onSuccess: noop,
        onError: noop,
      }),
    ).toThrow(uploadCopy.errors.notStarted);
  });

  it("uploads an R2 target in parts", () => {
    const upload = createS3ClipUpload({
      file: new File(["bytes"], "clip.mp4"),
      response: {
        ...prepared,
        upload: {
          ...prepared.upload,
          protocol: "s3-multipart",
          multipart: { uploadId: "upload-1", partSize: 8, partCount: 1 },
        },
      },
      resumeKey: "key",
      onProgress: noop,
      onSuccess: noop,
      onError: noop,
    });

    expect(upload).toBeInstanceOf(S3MultipartUpload);
  });
});
