import type {
  CompletedUploadPart,
  CreateClipResponse,
  MultipartUploadInfo,
  PresignedUploadParts,
  UploadedPart,
} from "@/api-client";
import { ApiError } from "@/shared/services/api-error";
import { createMultipartUploadApi } from "@/shared/services/apiClients";
import { uploadCopy } from "@/copy/upload";

/** The API's side of a multipart upload (`api/Video/MultipartUploadEndpoints.cs`). */
export type MultipartUploadApi = {
  presignParts: (
    clipId: string,
    uploadId: string,
    partNumbers: number[],
  ) => Promise<PresignedUploadParts>;
  listParts: (clipId: string, uploadId: string) => Promise<UploadedPart[]>;
  complete: (
    clipId: string,
    uploadId: string,
    parts: CompletedUploadPart[],
  ) => Promise<void>;
};

/** What a reload needs to pick an upload back up: the clip, the upload and the parts already sent. */
export type SavedMultipartUpload = {
  clipId: string;
  uploadId: string;
  partSize: number;
  partCount: number;
  /** ETags by part number. */
  parts: Record<number, string>;
  savedAt: number;
};

export type ResumeStore = {
  load: (key: string) => SavedMultipartUpload | null;
  save: (key: string, upload: SavedMultipartUpload) => void;
  remove: (key: string) => void;
};

export type S3MultipartUploadOptions = {
  file: File;
  clipId: string;
  multipart: MultipartUploadInfo;
  /** Where progress is kept for resuming after a reload; see `multipartResumeKey`. */
  resumeKey: string;
  onProgress: (uploaded: number, total: number) => void;
  onSuccess: () => void;
  onError: (error: Error) => void;
  api?: MultipartUploadApi;
  store?: ResumeStore;
  fetch?: typeof fetch;
  sleep?: (ms: number) => Promise<void>;
  now?: () => number;
  /** Parts sent at once. */
  concurrency?: number;
  /** Waits between attempts of one part, or of an API call that may pass on a second try. */
  retryDelays?: number[];
};

const PART_URL_BATCH = 10;
/** A part URL this close to expiring is asked for again rather than risked. */
const PART_URL_MARGIN_MS = 5 * 60 * 1000;
/** The bucket's lifecycle rule aborts unfinished uploads after a day; give up on one a little before that. */
const RESUME_MAX_AGE_MS = 23 * 60 * 60 * 1000;
const RESUME_PREFIX = "reelshelf:clip-upload:";

/** One file's upload in one book: the same file dropped on the same book after a reload resumes it. */
export function multipartResumeKey(
  categoryId: string,
  md5Hash: string,
  fileSize: number,
) {
  return `${RESUME_PREFIX}${categoryId}:${md5Hash}:${fileSize}`;
}

/** localStorage, which may be missing or refuse (private windows, full or blocked storage): then nothing resumes. */
export const localResumeStore: ResumeStore = {
  load(key) {
    try {
      const raw = window.localStorage.getItem(key);
      return raw ? (JSON.parse(raw) as SavedMultipartUpload) : null;
    } catch {
      return null;
    }
  },
  save(key, upload) {
    try {
      window.localStorage.setItem(key, JSON.stringify(upload));
    } catch {
      // Resuming is a convenience; the upload carries on without it.
    }
  },
  remove(key) {
    try {
      window.localStorage.removeItem(key);
    } catch {
      // As above.
    }
  },
};

export const defaultMultipartUploadApi: MultipartUploadApi = {
  presignParts: (clipId, uploadId, partNumbers) =>
    createMultipartUploadApi().presignUploadParts({
      clipId,
      presignUploadPartsRequest: { uploadId, partNumbers },
    }),
  listParts: async (clipId, uploadId) =>
    (await createMultipartUploadApi().listUploadParts({ clipId, uploadId }))
      .parts,
  complete: async (clipId, uploadId, parts) => {
    await createMultipartUploadApi().completeUpload({
      clipId,
      completeUploadRequest: { uploadId, parts },
    });
  },
};

const wait = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

/** An API error worth one more try: the network, a 5xx (503 is a check that couldn't finish) or a 429. */
function isRetryableApiError(error: unknown) {
  if (!(error instanceof ApiError)) return true;
  return error.status === 0 || error.status === 429 || error.status >= 500;
}

class PartUploadError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

/**
 * A saved upload this browser can pick up again, as the prepared clip it belongs to, or null. The API is
 * asked first: a clip that moved on (409) or an upload that is gone (404) is forgotten, and for the latter the
 * clip waiting on it is deleted so the file can be prepared afresh.
 */
export async function resumeSavedMultipartUpload({
  resumeKey,
  api = defaultMultipartUploadApi,
  store = localResumeStore,
  now = Date.now,
  deleteClip,
}: {
  resumeKey: string;
  api?: MultipartUploadApi;
  store?: ResumeStore;
  now?: () => number;
  deleteClip: (clipId: string) => Promise<void>;
}): Promise<CreateClipResponse | null> {
  const saved = store.load(resumeKey);
  if (!saved) return null;
  if (
    typeof saved.clipId !== "string" ||
    typeof saved.uploadId !== "string" ||
    !(saved.partSize > 0) ||
    !(saved.partCount > 0) ||
    now() - saved.savedAt > RESUME_MAX_AGE_MS
  ) {
    store.remove(resumeKey);
    return null;
  }

  try {
    await api.listParts(saved.clipId, saved.uploadId);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      store.remove(resumeKey);
      await deleteClip(saved.clipId).catch(() => undefined);
      return null;
    }
    if (error instanceof ApiError && error.status === 409) {
      store.remove(resumeKey);
      return null;
    }
    throw error;
  }

  return {
    clipId: saved.clipId,
    videoId: saved.clipId,
    upload: {
      protocol: "s3-multipart",
      endpoint: "",
      headers: {},
      metadata: {},
      expiresAt: new Date(saved.savedAt + RESUME_MAX_AGE_MS),
      multipart: {
        uploadId: saved.uploadId,
        partSize: saved.partSize,
        partCount: saved.partCount,
      },
    },
  };
}

/**
 * A presigned S3 multipart upload straight to storage: the file is cut into the API's part size, a few parts go
 * up at once to URLs the API presigns in small batches, each part is retried with backoff, and the API joins and
 * checks the file at the end. Finished parts and their ETags are kept under `resumeKey`, so after a reload the
 * same file picks up where it stopped (the API's part list is the truth). `abort` stops it like a pause, and
 * `start` carries on; cancelling for good is deleting the clip, which aborts the upload server-side.
 */
export class S3MultipartUpload {
  private readonly options: Required<
    Omit<S3MultipartUploadOptions, "onProgress" | "onSuccess" | "onError">
  > &
    Pick<S3MultipartUploadOptions, "onProgress" | "onSuccess" | "onError">;
  private readonly done = new Map<number, string>();
  private readonly urls = new Map<number, { url: string; expiresAt: number }>();
  private batch: Promise<void> | null = null;
  private controller: AbortController | null = null;
  private reconciled = false;
  private running = false;

  constructor(options: S3MultipartUploadOptions) {
    this.options = {
      api: defaultMultipartUploadApi,
      store: localResumeStore,
      fetch: (input, init) => fetch(input, init),
      sleep: wait,
      now: Date.now,
      concurrency: 4,
      retryDelays: [1000, 3000, 5000, 10000, 20000],
      ...options,
    };
  }

  start() {
    if (this.running) return;
    this.running = true;
    this.controller = new AbortController();
    const { signal } = this.controller;
    void this.run(signal).finally(() => {
      this.running = false;
    });
  }

  abort() {
    this.controller?.abort();
  }

  private get uploadId() {
    return this.options.multipart.uploadId;
  }

  private partRange(partNumber: number) {
    const { partSize } = this.options.multipart;
    const start = (partNumber - 1) * partSize;
    return { start, end: Math.min(start + partSize, this.options.file.size) };
  }

  private uploadedBytes() {
    let bytes = 0;
    for (const partNumber of this.done.keys()) {
      const { start, end } = this.partRange(partNumber);
      bytes += end - start;
    }
    return bytes;
  }

  private reportProgress() {
    this.options.onProgress(this.uploadedBytes(), this.options.file.size);
  }

  private save() {
    const { clipId, multipart, resumeKey, store, now } = this.options;
    store.save(resumeKey, {
      clipId,
      uploadId: multipart.uploadId,
      partSize: multipart.partSize,
      partCount: multipart.partCount,
      parts: Object.fromEntries(this.done),
      savedAt: store.load(resumeKey)?.savedAt ?? now(),
    });
  }

  private async run(signal: AbortSignal) {
    try {
      if (!this.reconciled) {
        await this.reconcile();
        this.reconciled = true;
      }
      this.reportProgress();

      const pending: number[] = [];
      for (let n = 1; n <= this.options.multipart.partCount; n++) {
        if (!this.done.has(n)) pending.push(n);
      }
      await this.uploadParts(pending, signal);
      if (signal.aborted) return;

      const parts = [...this.done.entries()]
        .sort(([a], [b]) => a - b)
        .map(([partNumber, etag]) => ({ partNumber, etag }));
      await this.withApiRetries(
        () =>
          this.options.api.complete(this.options.clipId, this.uploadId, parts),
        signal,
      );
      this.options.store.remove(this.options.resumeKey);
      this.reportProgress();
      this.options.onSuccess();
    } catch (error) {
      if (signal.aborted) return;
      this.options.onError(
        error instanceof Error ? error : new Error(uploadCopy.errors.failed),
      );
    }
  }

  /**
   * Takes the parts storage already holds as done, when this upload was saved before (a reload, or a retry in
   * this tab), and records the upload so a reload from here on finds it.
   */
  private async reconcile() {
    const { store, resumeKey, clipId } = this.options;
    const saved = store.load(resumeKey);
    if (saved?.uploadId === this.uploadId) {
      const listed = await this.withApiRetries(() =>
        this.options.api.listParts(clipId, this.uploadId),
      );
      for (const part of listed) {
        if (
          part.partNumber < 1 ||
          part.partNumber > this.options.multipart.partCount
        ) {
          continue;
        }
        // A part cut short (a PUT that died half way never lands, but be sure) is sent again.
        const { start, end } = this.partRange(part.partNumber);
        if (part.size === end - start)
          this.done.set(part.partNumber, part.etag);
      }
    }
    this.save();
  }

  /** Sends the parts with a few at a time, stopping the rest when one fails for good. */
  private async uploadParts(partNumbers: number[], signal: AbortSignal) {
    const queue = [...partNumbers];
    let failure: unknown = null;
    const worker = async () => {
      while (queue.length && !signal.aborted && failure === null) {
        const partNumber = queue.shift()!;
        try {
          await this.uploadPart(partNumber, queue, signal);
        } catch (error) {
          failure ??= error;
        }
      }
    };
    await Promise.all(
      Array.from(
        { length: Math.min(this.options.concurrency, queue.length) },
        worker,
      ),
    );
    if (failure !== null) throw failure;
  }

  private async uploadPart(
    partNumber: number,
    upcoming: number[],
    signal: AbortSignal,
  ) {
    const { retryDelays, file } = this.options;
    const { start, end } = this.partRange(partNumber);
    for (let attempt = 0; ; attempt++) {
      try {
        const url = await this.partUrl(partNumber, upcoming);
        const response = await this.options.fetch(url, {
          method: "PUT",
          body: file.slice(start, end),
          signal,
        });
        if (!response.ok) {
          console.warn(
            `Upload part ${partNumber} was refused (${response.status})`,
          );
          throw new PartUploadError(
            uploadCopy.errors.partFailed,
            response.status,
          );
        }
        const etag = response.headers.get("ETag");
        if (!etag) {
          // The bucket's CORS must expose ETag; without it the upload can't be completed.
          console.warn(`Storage sent no ETag for upload part ${partNumber}`);
          throw new PartUploadError(uploadCopy.errors.partFailed, 0);
        }
        this.done.set(partNumber, etag);
        this.save();
        this.reportProgress();
        return;
      } catch (error) {
        if (signal.aborted) throw error;
        // An expired or refused URL is asked for afresh on the next attempt.
        if (error instanceof PartUploadError && error.status === 403) {
          this.urls.delete(partNumber);
        }
        if (
          attempt >= retryDelays.length ||
          (error instanceof ApiError && !isRetryableApiError(error)) ||
          // The upload itself is gone or the request is wrong; another try won't change that.
          (error instanceof PartUploadError &&
            (error.status === 400 || error.status === 404))
        ) {
          throw error;
        }
        await this.options.sleep(retryDelays[attempt]);
      }
    }
  }

  /** The part's URL, asking for it with the next few parts' when there is none still fresh. */
  private async partUrl(partNumber: number, upcoming: number[]) {
    for (;;) {
      const cached = this.urls.get(partNumber);
      if (
        cached &&
        cached.expiresAt - this.options.now() > PART_URL_MARGIN_MS
      ) {
        return cached.url;
      }
      if (this.batch) {
        await this.batch.catch(() => undefined);
        continue;
      }
      const numbers = [
        partNumber,
        ...upcoming.filter((n) => n !== partNumber && !this.hasFreshUrl(n)),
      ].slice(0, PART_URL_BATCH);
      this.batch = this.presign(numbers);
      try {
        await this.batch;
      } finally {
        this.batch = null;
      }
      const fresh = this.urls.get(partNumber);
      if (!fresh) {
        console.warn(`No URL came back for upload part ${partNumber}`);
        throw new Error(uploadCopy.errors.partFailed);
      }
      return fresh.url;
    }
  }

  private hasFreshUrl(partNumber: number) {
    const cached = this.urls.get(partNumber);
    return (
      !!cached && cached.expiresAt - this.options.now() > PART_URL_MARGIN_MS
    );
  }

  private async presign(partNumbers: number[]) {
    const presigned = await this.options.api.presignParts(
      this.options.clipId,
      this.uploadId,
      partNumbers,
    );
    const expiresAt = new Date(presigned.expiresAt).getTime();
    for (const part of presigned.parts) {
      this.urls.set(part.partNumber, { url: part.url, expiresAt });
    }
  }

  private async withApiRetries<T>(
    call: () => Promise<T>,
    signal?: AbortSignal,
  ): Promise<T> {
    const { retryDelays } = this.options;
    for (let attempt = 0; ; attempt++) {
      try {
        return await call();
      } catch (error) {
        if (
          signal?.aborted ||
          attempt >= retryDelays.length ||
          !isRetryableApiError(error)
        ) {
          throw error;
        }
        await this.options.sleep(retryDelays[attempt]);
      }
    }
  }
}
