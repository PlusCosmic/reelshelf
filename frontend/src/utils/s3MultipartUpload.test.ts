import { describe, expect, it, vi } from "vitest";
import type { UploadedPart } from "@/api-client";
import { ApiError } from "@/shared/services/api-error";
import {
  localResumeStore,
  multipartResumeKey,
  resumeSavedMultipartUpload,
  S3MultipartUpload,
  type MultipartUploadApi,
  type ResumeStore,
  type SavedMultipartUpload,
} from "./s3MultipartUpload";

// 20 bytes in parts of 8: 8, 8 and 4.
const FILE_SIZE = 20;
const multipart = { uploadId: "upload-1", partSize: 8, partCount: 3 };
const resumeKey = multipartResumeKey("apex", "md5", FILE_SIZE);
const NOW = Date.parse("2026-10-18T12:00:00Z");

function memoryStore(initial?: SavedMultipartUpload): ResumeStore & {
  entries: Map<string, SavedMultipartUpload>;
} {
  const entries = new Map<string, SavedMultipartUpload>();
  if (initial) entries.set(resumeKey, initial);
  return {
    entries,
    load: (key) => entries.get(key) ?? null,
    save: (key, upload) => entries.set(key, structuredClone(upload)),
    remove: (key) => entries.delete(key),
  };
}

function fakeApi(listed: UploadedPart[] = []) {
  return {
    presignParts: vi.fn(
      async (_clipId: string, _uploadId: string, partNumbers: number[]) => ({
        parts: partNumbers.map((partNumber) => ({
          partNumber,
          url: `https://r2.example/uploads/clip-1?partNumber=${partNumber}`,
        })),
        expiresAt: new Date(NOW + 60 * 60 * 1000),
      }),
    ),
    listParts: vi.fn(async () => listed),
    complete: vi.fn(async () => undefined),
  } satisfies MultipartUploadApi;
}

/** Storage that answers each part's PUT with an ETag, a little later, counting how many are open at once. */
function fakeStorage(failures: Record<number, number> = {}) {
  const state = { open: 0, maxOpen: 0, sizes: new Map<number, number>() };
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const partNumber = Number(
      new URL(String(input)).searchParams.get("partNumber"),
    );
    state.open++;
    state.maxOpen = Math.max(state.maxOpen, state.open);
    await new Promise((resolve) => setTimeout(resolve, 1));
    state.open--;
    if (init?.signal?.aborted) throw new DOMException("Aborted", "AbortError");
    if ((failures[partNumber] ?? 0) > 0) {
      failures[partNumber]--;
      return new Response(null, { status: 500 });
    }
    state.sizes.set(partNumber, (init?.body as Blob).size);
    return new Response(null, {
      status: 200,
      headers: { ETag: `"etag-${partNumber}"` },
    });
  });
  return { fetch, state };
}

function run(options: {
  api: MultipartUploadApi;
  store: ResumeStore;
  fetch: typeof fetch;
  sleep?: (ms: number) => Promise<void>;
  concurrency?: number;
}) {
  const progress: [number, number][] = [];
  let upload!: S3MultipartUpload;
  const finished = new Promise<Error | null>((resolve) => {
    upload = new S3MultipartUpload({
      file: new File([new Uint8Array(FILE_SIZE)], "clip.mp4"),
      clipId: "clip-1",
      multipart,
      resumeKey,
      now: () => NOW,
      sleep: options.sleep ?? (async () => undefined),
      retryDelays: [100, 200, 400],
      onProgress: (uploaded, total) => progress.push([uploaded, total]),
      onSuccess: () => resolve(null),
      onError: (error) => resolve(error),
      ...options,
    });
  });
  return { upload, finished, progress };
}

const allParts = [
  { partNumber: 1, etag: '"etag-1"' },
  { partNumber: 2, etag: '"etag-2"' },
  { partNumber: 3, etag: '"etag-3"' },
];

describe("S3MultipartUpload", () => {
  it("sends every part in parallel, then completes with their ETags", async () => {
    const api = fakeApi();
    const store = memoryStore();
    const storage = fakeStorage();
    const { upload, finished, progress } = run({
      api,
      store,
      fetch: storage.fetch,
      concurrency: 2,
    });

    upload.start();

    expect(await finished).toBeNull();
    expect([...storage.state.sizes.entries()].sort()).toEqual([
      [1, 8],
      [2, 8],
      [3, 4],
    ]);
    expect(storage.state.maxOpen).toBe(2);
    // One batch of URLs for all three parts, and nothing to list for an upload this browser never saw.
    expect(api.presignParts).toHaveBeenCalledTimes(1);
    expect(api.presignParts).toHaveBeenCalledWith(
      "clip-1",
      "upload-1",
      [1, 2, 3],
    );
    expect(api.listParts).not.toHaveBeenCalled();
    expect(api.complete).toHaveBeenCalledWith("clip-1", "upload-1", allParts);
    expect(progress[0]).toEqual([0, FILE_SIZE]);
    expect(progress.at(-1)).toEqual([FILE_SIZE, FILE_SIZE]);
    // Done, so nothing is left to resume.
    expect(store.entries.size).toBe(0);
  });

  it("keeps each finished part for a reload while it goes", async () => {
    const store = memoryStore();
    const saves: SavedMultipartUpload[] = [];
    const save = store.save;
    store.save = (key, value) => {
      saves.push(structuredClone(value));
      save(key, value);
    };
    const { upload, finished } = run({
      api: fakeApi(),
      store,
      fetch: fakeStorage().fetch,
      concurrency: 1,
    });

    upload.start();
    await finished;

    expect(saves[0]).toEqual({
      clipId: "clip-1",
      uploadId: "upload-1",
      partSize: 8,
      partCount: 3,
      parts: {},
      savedAt: NOW,
    });
    expect(saves.at(-1)?.parts).toEqual({
      1: '"etag-1"',
      2: '"etag-2"',
      3: '"etag-3"',
    });
  });

  it("retries a failing part with backoff", async () => {
    const sleep = vi.fn(async () => undefined);
    const storage = fakeStorage({ 2: 2 });
    const api = fakeApi();
    const { upload, finished } = run({
      api,
      store: memoryStore(),
      fetch: storage.fetch,
      sleep,
    });

    upload.start();

    expect(await finished).toBeNull();
    expect(sleep.mock.calls).toEqual([[100], [200]]);
    expect(api.complete).toHaveBeenCalledWith("clip-1", "upload-1", allParts);
  });

  it("gives up on a part that keeps failing, without completing", async () => {
    const api = fakeApi();
    const { upload, finished } = run({
      api,
      store: memoryStore(),
      fetch: fakeStorage({ 3: 99 }).fetch,
    });

    upload.start();

    expect((await finished)?.message).toBe("Part 3 was refused (500).");
    expect(api.complete).not.toHaveBeenCalled();
  });

  it("resumes after a reload, sending only the parts storage doesn't have", async () => {
    // The browser saved part 1 only; storage also has part 2, and a part 3 cut short.
    const store = memoryStore({
      clipId: "clip-1",
      ...multipart,
      parts: { 1: '"etag-1"' },
      savedAt: NOW - 1000,
    });
    const api = fakeApi([
      { partNumber: 1, etag: '"etag-1"', size: 8 },
      { partNumber: 2, etag: '"etag-2"', size: 8 },
      { partNumber: 3, etag: '"etag-short"', size: 2 },
    ]);
    const storage = fakeStorage();
    const { upload, finished, progress } = run({
      api,
      store,
      fetch: storage.fetch,
    });

    upload.start();

    expect(await finished).toBeNull();
    expect(api.listParts).toHaveBeenCalledWith("clip-1", "upload-1");
    expect([...storage.state.sizes.keys()]).toEqual([3]);
    expect(api.presignParts).toHaveBeenCalledWith("clip-1", "upload-1", [3]);
    expect(api.complete).toHaveBeenCalledWith("clip-1", "upload-1", allParts);
    expect(progress[0]).toEqual([16, FILE_SIZE]);
  });

  it("stops quietly on abort, and carries on from there on start", async () => {
    const api = fakeApi();
    const storage = fakeStorage();
    const onError = vi.fn();
    let resolveDone!: () => void;
    const done = new Promise<void>((resolve) => (resolveDone = resolve));
    const upload = new S3MultipartUpload({
      file: new File([new Uint8Array(FILE_SIZE)], "clip.mp4"),
      clipId: "clip-1",
      multipart,
      resumeKey,
      api,
      store: memoryStore(),
      fetch: storage.fetch,
      now: () => NOW,
      sleep: async () => undefined,
      onProgress: () => undefined,
      onSuccess: resolveDone,
      onError,
    });

    upload.start();
    upload.abort();
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(api.complete).not.toHaveBeenCalled();

    upload.start();
    await done;

    expect(onError).not.toHaveBeenCalled();
    expect(api.complete).toHaveBeenCalledWith("clip-1", "upload-1", allParts);
  });

  it("asks for a part's URL again when storage refuses the old one", async () => {
    const api = fakeApi();
    let refused = false;
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const partNumber = new URL(String(input)).searchParams.get("partNumber");
      if (partNumber === "1" && !refused) {
        refused = true;
        return new Response(null, { status: 403 });
      }
      return new Response(null, {
        status: 200,
        headers: { ETag: `"etag-${partNumber}"` },
      });
    });
    const { upload, finished } = run({
      api,
      store: memoryStore(),
      fetch,
      concurrency: 1,
    });

    upload.start();

    expect(await finished).toBeNull();
    expect(api.presignParts).toHaveBeenNthCalledWith(
      1,
      "clip-1",
      "upload-1",
      [1, 2, 3],
    );
    expect(api.presignParts).toHaveBeenNthCalledWith(
      2,
      "clip-1",
      "upload-1",
      [1],
    );
  });

  it("fails when the API won't complete the file, with the API's reason", async () => {
    const api = fakeApi();
    api.complete.mockRejectedValue(
      new ApiError("The file has no video in it.", 422, "validation"),
    );
    const { upload, finished } = run({
      api,
      store: memoryStore(),
      fetch: fakeStorage().fetch,
    });

    upload.start();

    expect((await finished)?.message).toBe("The file has no video in it.");
    expect(api.complete).toHaveBeenCalledTimes(1);
  });

  it("tries completing again when the check couldn't finish", async () => {
    const api = fakeApi();
    api.complete
      .mockRejectedValueOnce(new ApiError("Try again", 503, "unexpected"))
      .mockResolvedValueOnce(undefined);
    const { upload, finished } = run({
      api,
      store: memoryStore(),
      fetch: fakeStorage().fetch,
    });

    upload.start();

    expect(await finished).toBeNull();
    expect(api.complete).toHaveBeenCalledTimes(2);
  });
});

describe("resumeSavedMultipartUpload", () => {
  const saved: SavedMultipartUpload = {
    clipId: "clip-1",
    ...multipart,
    parts: { 1: '"etag-1"' },
    savedAt: NOW - 60_000,
  };

  it("picks up an upload the API still has open", async () => {
    const deleteClip = vi.fn(async () => undefined);

    const response = await resumeSavedMultipartUpload({
      resumeKey,
      api: fakeApi(),
      store: memoryStore(saved),
      now: () => NOW,
      deleteClip,
    });

    expect(response?.clipId).toBe("clip-1");
    expect(response?.upload.protocol).toBe("s3-multipart");
    expect(response?.upload.multipart).toEqual(multipart);
    expect(deleteClip).not.toHaveBeenCalled();
  });

  it("forgets an upload that is gone and deletes the clip waiting on it", async () => {
    const api = fakeApi();
    api.listParts.mockRejectedValue(new ApiError("Gone", 404, "not-found"));
    const store = memoryStore(saved);
    const deleteClip = vi.fn(async () => undefined);

    const response = await resumeSavedMultipartUpload({
      resumeKey,
      api,
      store,
      now: () => NOW,
      deleteClip,
    });

    expect(response).toBeNull();
    expect(deleteClip).toHaveBeenCalledWith("clip-1");
    expect(store.entries.size).toBe(0);
  });

  it("forgets a clip that has moved on, without deleting it", async () => {
    const api = fakeApi();
    api.listParts.mockRejectedValue(new ApiError("Done", 409, "conflict"));
    const store = memoryStore(saved);
    const deleteClip = vi.fn(async () => undefined);

    const response = await resumeSavedMultipartUpload({
      resumeKey,
      api,
      store,
      now: () => NOW,
      deleteClip,
    });

    expect(response).toBeNull();
    expect(deleteClip).not.toHaveBeenCalled();
    expect(store.entries.size).toBe(0);
  });

  it("ignores an upload older than the bucket keeps them", async () => {
    const api = fakeApi();
    const old = memoryStore({ ...saved, savedAt: NOW - 24 * 60 * 60 * 1000 });

    expect(
      await resumeSavedMultipartUpload({
        resumeKey,
        api,
        store: old,
        now: () => NOW,
        deleteClip: vi.fn(),
      }),
    ).toBeNull();
    expect(old.entries.size).toBe(0);
    expect(api.listParts).not.toHaveBeenCalled();
  });
});

describe("localResumeStore", () => {
  it("does without storage it can't reach", () => {
    // No window.localStorage here, as in some private windows: nothing is kept, and nothing throws.
    expect(() =>
      localResumeStore.save(resumeKey, {
        clipId: "clip-1",
        ...multipart,
        parts: {},
        savedAt: NOW,
      }),
    ).not.toThrow();
    expect(localResumeStore.load(resumeKey)).toBeNull();
    expect(() => localResumeStore.remove(resumeKey)).not.toThrow();
  });
});
