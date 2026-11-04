import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  buildBulkUploadQueue,
  type BulkUploadFileInput,
  type BulkUploadRejectedFile,
} from "@/hooks/bulkUploadQueue";
import { planQueryKey } from "@/hooks/auth.queries";
import { fetchPlan } from "@/shared/services/plans";
import { clipLengthProblem, readVideoDuration } from "@/utils/clipLength";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import { ApiError } from "@/shared/services/api-error";
import { deleteClip, findDuplicateVideos } from "@/shared/services/clips";
import { createDuplicateChecker } from "@/utils/duplicateCheck";
import { ensureGamingSessionPlaylist } from "@/shared/services/playlists";
import { uploadErrorMessage } from "@/utils/uploadErrorMessage";
import {
  isActiveUpload,
  isHashingUpload,
  isUnfinishedUpload,
  type UploadItem,
} from "./addClipsModel";
import { uploadCopy } from "@/copy/upload";

const MAX_ACTIVE_UPLOADS = 3;
// Hashing is limited by the disk as much as the CPU, so a few workers are enough; leave a core for the page.
const HASH_CONCURRENCY = Math.max(
  1,
  Math.min(4, (globalThis.navigator?.hardwareConcurrency || 2) - 1),
);

type UploadsContextValue = {
  items: UploadItem[];
  paused: boolean;
  /** Queues files for one book and starts them; returns the files that can't be uploaded. */
  addClips: (
    inputs: BulkUploadFileInput[],
    categoryId: string,
  ) => BulkUploadRejectedFile[];
  pauseAll: () => void;
  resumeAll: () => void;
  retry: (id: string) => void;
  clearFinished: () => void;
  uploadForClip: (clipId: string) => UploadItem | undefined;
};

const UploadsContext = createContext<UploadsContextValue | null>(null);

/** The upload behind a clip, where this tab is uploading it; safe outside the provider. */
export function useUploadForClip(clipId: string) {
  return useContext(UploadsContext)?.uploadForClip(clipId);
}

export function useUploads() {
  const value = useContext(UploadsContext);
  if (!value) throw new Error("useUploads needs an UploadsProvider");
  return value;
}

/**
 * Uploads that keep going while the owner moves around the app. Files are given a book when they
 * are dropped, start at once, and are filed into their gaming session's collection once the last
 * clip of that session has reached its video host. Processing then happens there, so only the uploads
 * need the tab open.
 */
export function UploadsProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [items, setItems] = useState<UploadItem[]>([]);
  const [paused, setPaused] = useState(false);
  // tus for Bunny, S3 multipart for R2: both pause with abort and carry on with start. An upload is here from the
  // moment it starts (or would, but for a pause) until it succeeds or fails.
  const uploads = useRef(
    new Map<string, { start: () => void; abort: () => unknown }>(),
  );
  // Read by uploads still being prepared when they would start, so a pause meanwhile holds them back too.
  const pausedRef = useRef(false);
  // The uploads a pause stopped or held back, which a resume starts.
  const pausedUploads = useRef(new Set<string>());
  const preparedClipIds = useRef(new Map<string, string>());
  // The item each file (by book and hash) is queued as, so the same file isn't uploaded twice into a book at once.
  const duplicateKeys = useRef(new Map<string, string>());
  const freshIds = useRef(new Set<string>());
  const filedIds = useRef(new Set<string>());
  const filingSessions = useRef(new Set<string>());
  const batch = useRef(0);
  // One request per book for the hashes that finish close together, rather than a clip preparation each.
  const [checkDuplicate] = useState(() =>
    createDuplicateChecker(findDuplicateVideos),
  );

  const patch = useCallback((id: string, change: Partial<UploadItem>) => {
    setItems((current) =>
      current.map((item) => (item.id === id ? { ...item, ...change } : item)),
    );
  }, []);

  /** Lets the same file be added to its book again once its item has failed, been retried or been cleared. */
  const forgetDuplicateKey = useCallback((id: string) => {
    for (const [key, owner] of duplicateKeys.current) {
      if (owner === id) duplicateKeys.current.delete(key);
    }
  }, []);

  /** Gives back the storage a clip reserved when its upload never finished. */
  const releasePreparedClip = useCallback(
    async (id: string) => {
      const clipId = preparedClipIds.current.get(id);
      if (!clipId) return;
      try {
        await deleteClip(clipId);
      } catch (error) {
        // Already gone; the server also purges never-uploaded clips after a day.
        if (!(error instanceof ApiError && error.status === 404)) return;
      }
      preparedClipIds.current.delete(id);
      freshIds.current.add(id);
      void queryClient.invalidateQueries({ queryKey: planQueryKey });
      invalidateClipCollections(queryClient);
    },
    [queryClient],
  );

  /**
   * Checks the file's length against the plan, hashes it and asks the API whether the book already has it, ahead of
   * the upload slots; a file that passes waits as `hashed` for one.
   */
  const prepare = useCallback(
    (item: UploadItem) => {
      patch(item.id, { status: "hashing", progress: 0, error: null });

      void (async () => {
        try {
          // Refuse a clip longer than the plan keeps before uploading it; the API checks the file again.
          const plan = await queryClient.fetchQuery({
            queryKey: planQueryKey,
            queryFn: fetchPlan,
            staleTime: 30_000,
          });
          const lengthProblem = clipLengthProblem(
            await readVideoDuration(item.file),
            plan.maxClipSeconds,
            plan.billingEnabled,
          );
          if (lengthProblem) {
            patch(item.id, { status: "error", error: lengthProblem });
            return;
          }

          // The hashing code loads with the first upload rather than with the library.
          const [{ hashFileMd5 }, { localResumeStore, multipartResumeKey }] =
            await Promise.all([
              import("@/utils/fileHash"),
              import("@/utils/s3MultipartUpload"),
            ]);
          const md5Hash = await hashFileMd5(item.file);
          const duplicateKey = `${item.categoryId}:${md5Hash}`;
          const existing = duplicateKeys.current.get(duplicateKey);
          if (existing && existing !== item.id) {
            patch(item.id, {
              status: "duplicate",
              error: uploadCopy.duplicateInBook,
            });
            return;
          }
          duplicateKeys.current.set(duplicateKey, item.id);

          // An R2 upload saved before a reload already reserved its clip with this hash; it resumes rather than
          // counting as a duplicate.
          const resuming = localResumeStore.load(
            multipartResumeKey(item.categoryId, md5Hash, item.file.size),
          );
          if (!resuming && (await checkDuplicate(item.categoryId, md5Hash))) {
            patch(item.id, {
              status: "duplicate",
              md5Hash,
              error: uploadCopy.errors.alreadyUploaded,
            });
            return;
          }
          patch(item.id, { status: "hashed", md5Hash });
        } catch (error) {
          forgetDuplicateKey(item.id);
          patch(item.id, {
            status: "error",
            error: uploadErrorMessage(error),
          });
        }
      })();
    },
    [checkDuplicate, forgetDuplicateKey, patch, queryClient],
  );

  const start = useCallback(
    (item: UploadItem) => {
      const md5Hash = item.md5Hash;
      if (!md5Hash) return;
      patch(item.id, { status: "creating", error: null });

      void (async () => {
        try {
          // The upload clients (tus, S3 multipart) load with the first upload rather than with the library.
          const [
            {
              createPreparedClipUpload,
              createS3ClipUpload,
              createTusClipUpload,
              isS3MultipartUpload,
            },
            {
              localResumeStore,
              multipartResumeKey,
              resumeSavedMultipartUpload,
            },
          ] = await Promise.all([
            import("@/utils/clipUpload"),
            import("@/utils/s3MultipartUpload"),
          ]);
          // A multipart upload this browser started before a reload carries on into the clip it prepared then.
          const resumeKey = multipartResumeKey(
            item.categoryId,
            md5Hash,
            item.file.size,
          );
          const response =
            (await resumeSavedMultipartUpload({ resumeKey, deleteClip })) ??
            (await createPreparedClipUpload({
              categoryId: item.categoryId,
              createdAt: item.createdAt,
              fileSize: item.file.size,
              md5Hash,
              title: item.title,
            }));
          preparedClipIds.current.set(item.id, response.clipId);
          // The clip is in the book from here on, so it shows in the list while it uploads.
          patch(item.id, { clipId: response.clipId });
          void queryClient.invalidateQueries({
            queryKey: planQueryKey,
          });
          invalidateClipCollections(queryClient);

          const callbacks = {
            onProgress: (uploaded: number, total: number) =>
              patch(item.id, {
                progress: total > 0 ? Math.round((uploaded / total) * 100) : 0,
              }),
            onSuccess: () => {
              uploads.current.delete(item.id);
              pausedUploads.current.delete(item.id);
              preparedClipIds.current.delete(item.id);
              patch(item.id, { status: "uploaded", progress: 100 });
              invalidateClipCollections(queryClient);
            },
            onError: (error: Error) => {
              uploads.current.delete(item.id);
              pausedUploads.current.delete(item.id);
              forgetDuplicateKey(item.id);
              localResumeStore.remove(resumeKey);
              void releasePreparedClip(item.id);
              patch(item.id, {
                status: "error",
                error: uploadErrorMessage(error),
              });
            },
          };

          // Starts the upload, or holds it back as paused when a pause came while the clip was being prepared.
          const begin = (upload: {
            start: () => void;
            abort: () => unknown;
          }) => {
            uploads.current.set(item.id, upload);
            if (pausedRef.current) {
              pausedUploads.current.add(item.id);
              patch(item.id, { status: "paused" });
              return;
            }
            patch(item.id, { status: "uploading" });
            upload.start();
          };

          if (isS3MultipartUpload(response)) {
            begin(
              createS3ClipUpload({
                file: item.file,
                response,
                resumeKey,
                ...callbacks,
              }),
            );
            return;
          }

          const upload = createTusClipUpload({
            file: item.file,
            response,
            ...callbacks,
          });

          const previous = await upload.findPreviousUploads();
          if (freshIds.current.delete(item.id)) {
            await Promise.all(
              previous.map((entry) =>
                upload.options.urlStorage
                  ?.removeUpload(entry.urlStorageKey)
                  .catch(() => undefined),
              ),
            );
          } else if (previous.length) {
            upload.resumeFromPreviousUpload(previous[0]);
          }
          begin(upload);
        } catch (error) {
          forgetDuplicateKey(item.id);
          void releasePreparedClip(item.id);
          patch(item.id, {
            status:
              error instanceof ApiError && error.status === 409
                ? "duplicate"
                : "error",
            error: uploadErrorMessage(error),
          });
        }
      })();
    },
    [forgetDuplicateKey, patch, queryClient, releasePreparedClip],
  );

  // Hash a few files at once, each in its own worker, whether or not the upload slots are free.
  useEffect(() => {
    const hashing = items.filter(isHashingUpload).length;
    if (hashing >= HASH_CONCURRENCY) return;
    items
      .filter((item) => item.status === "queued")
      .slice(0, HASH_CONCURRENCY - hashing)
      .forEach(prepare);
  }, [items, prepare]);

  // Keep a few uploads running at once; the rest wait their turn.
  useEffect(() => {
    if (paused) return;
    const active = items.filter(isActiveUpload).length;
    if (active >= MAX_ACTIVE_UPLOADS) return;
    items
      .filter((item) => item.status === "hashed")
      .slice(0, MAX_ACTIVE_UPLOADS - active)
      .forEach(start);
  }, [items, paused, start]);

  // File each gaming session into its collection once nothing in it is still on its way up.
  useEffect(() => {
    const sessions = new Map<string, UploadItem[]>();
    for (const item of items) {
      const key = `${item.categoryId}:${item.sessionDate}`;
      sessions.set(key, [...(sessions.get(key) ?? []), item]);
    }
    for (const [key, sessionItems] of sessions) {
      if (filingSessions.current.has(key)) continue;
      if (sessionItems.some(isUnfinishedUpload)) continue;
      const toFile = sessionItems.filter(
        (item) =>
          item.status === "uploaded" &&
          item.clipId &&
          !filedIds.current.has(item.id),
      );
      if (!toFile.length) continue;

      filingSessions.current.add(key);
      toFile.forEach((item) => filedIds.current.add(item.id));
      const first = toFile[0];
      void ensureGamingSessionPlaylist({
        categoryId: first.categoryId,
        clipIds: toFile.map((item) => item.clipId!),
        sessionDate: new Date(`${first.sessionDate}T00:00:00`),
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
      })
        .then(() => invalidateClipCollections(queryClient))
        .catch(() => toFile.forEach((item) => filedIds.current.delete(item.id)))
        .finally(() => filingSessions.current.delete(key));
    }
  }, [items, queryClient]);

  // Leaving the site stops uploads still in this tab (an R2 one resumes when the file is added again);
  // processing at the video host carries on regardless.
  const uploading = items.some(isUnfinishedUpload);
  useEffect(() => {
    if (!uploading) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [uploading]);

  const addClips = useCallback(
    (inputs: BulkUploadFileInput[], categoryId: string) => {
      // With no categories to match against, every file goes to the book it was dropped on.
      const result = buildBulkUploadQueue(inputs, [], {
        fallbackCategoryId: categoryId,
      });
      batch.current += 1;
      const added: UploadItem[] = result.rows.map((row) => ({
        id: `${batch.current}:${row.id}`,
        file: row.file,
        title: row.title,
        categoryId,
        createdAt: row.createdAt,
        sessionDate: row.sessionDate,
        status: "queued",
        md5Hash: null,
        progress: 0,
        clipId: null,
        error: null,
      }));
      setItems((current) => [...current, ...added]);
      return result.rejected;
    },
    [],
  );

  // Uploads are stopped and started here, outside the state updaters, which StrictMode runs twice. Items still being
  // prepared (`creating`) see the pause when their upload would start and wait as `paused` instead.
  const pauseAll = useCallback(() => {
    pausedRef.current = true;
    setPaused(true);
    const stopped = new Set<string>();
    for (const [id, upload] of uploads.current) {
      if (pausedUploads.current.has(id)) continue;
      upload.abort();
      pausedUploads.current.add(id);
      stopped.add(id);
    }
    setItems((current) =>
      current.map((item) =>
        stopped.has(item.id) && item.status === "uploading"
          ? { ...item, status: "paused" }
          : item,
      ),
    );
  }, []);

  const resumeAll = useCallback(() => {
    pausedRef.current = false;
    setPaused(false);
    const resumed = new Set<string>();
    for (const id of pausedUploads.current) {
      const upload = uploads.current.get(id);
      if (!upload) continue;
      upload.start();
      resumed.add(id);
    }
    pausedUploads.current.clear();
    setItems((current) =>
      current.map((item) => {
        if (item.status !== "paused") return item;
        if (resumed.has(item.id)) return { ...item, status: "uploading" };
        return { ...item, status: item.md5Hash ? "hashed" : "queued" };
      }),
    );
  }, []);

  const retry = useCallback(
    (id: string) => {
      forgetDuplicateKey(id);
      setItems((current) =>
        current.map((item) =>
          item.id === id && item.status === "error"
            ? {
                ...item,
                // A file already hashed and checked goes straight back in line for an upload slot.
                status: item.md5Hash ? "hashed" : "queued",
                error: null,
                clipId: null,
              }
            : item,
        ),
      );
    },
    [forgetDuplicateKey],
  );

  const clearFinished = useCallback(() => {
    items
      .filter((item) => !isUnfinishedUpload(item))
      .forEach((item) => forgetDuplicateKey(item.id));
    setItems((current) => current.filter(isUnfinishedUpload));
  }, [forgetDuplicateKey, items]);

  const value = useMemo<UploadsContextValue>(() => {
    const byClip = new Map(
      items.flatMap((item): [string, UploadItem][] =>
        item.clipId ? [[item.clipId, item]] : [],
      ),
    );
    return {
      items,
      paused,
      addClips,
      pauseAll,
      resumeAll,
      retry,
      clearFinished,
      uploadForClip: (clipId) => byClip.get(clipId),
    };
  }, [addClips, clearFinished, items, pauseAll, paused, resumeAll, retry]);

  return (
    <UploadsContext.Provider value={value}>{children}</UploadsContext.Provider>
  );
}
