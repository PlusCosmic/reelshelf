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
import type * as tus from "tus-js-client";
import {
  buildBulkUploadQueue,
  type BulkUploadFileInput,
  type BulkUploadRejectedFile,
} from "@/hooks/bulkUploadQueue";
import { storageUsageQueryKey } from "@/hooks/auth.queries";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import { ApiError } from "@/shared/services/api-error";
import { deleteClip } from "@/shared/services/clips";
import { ensureGamingSessionPlaylist } from "@/shared/services/playlists";
import {
  calculateClipUploadMd5,
  createPreparedClipUpload,
  createTusClipUpload,
  uploadErrorMessage,
} from "@/utils/clipUpload";
import {
  isActiveUpload,
  isUnfinishedUpload,
  type UploadItem,
} from "./addClipsModel";

const MAX_ACTIVE_UPLOADS = 3;

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
 * clip of that session has reached Bunny. Processing then happens on Bunny, so only the uploads
 * need the tab open.
 */
export function UploadsProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [items, setItems] = useState<UploadItem[]>([]);
  const [paused, setPaused] = useState(false);
  const uploads = useRef(new Map<string, tus.Upload>());
  const preparedClipIds = useRef(new Map<string, string>());
  const duplicateKeys = useRef(new Map<string, string>());
  const freshIds = useRef(new Set<string>());
  const filedIds = useRef(new Set<string>());
  const filingSessions = useRef(new Set<string>());
  const batch = useRef(0);

  const patch = useCallback((id: string, change: Partial<UploadItem>) => {
    setItems((current) =>
      current.map((item) => (item.id === id ? { ...item, ...change } : item)),
    );
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
      void queryClient.invalidateQueries({ queryKey: storageUsageQueryKey });
      invalidateClipCollections(queryClient);
    },
    [queryClient],
  );

  const start = useCallback(
    (item: UploadItem) => {
      patch(item.id, { status: "hashing", progress: 0, error: null });

      void (async () => {
        try {
          const md5Hash = await calculateClipUploadMd5(item.file);
          const duplicateKey = `${item.categoryId}:${md5Hash}`;
          const existing = duplicateKeys.current.get(duplicateKey);
          if (existing && existing !== item.id) {
            patch(item.id, {
              status: "duplicate",
              error: "Already added to this book.",
            });
            return;
          }
          duplicateKeys.current.set(duplicateKey, item.id);
          patch(item.id, { status: "creating" });

          const response = await createPreparedClipUpload({
            categoryId: item.categoryId,
            createdAt: item.createdAt,
            fileSize: item.file.size,
            md5Hash,
            title: item.title,
          });
          preparedClipIds.current.set(item.id, response.clipId);
          // The clip is in the book from here on, so it shows in the list while it uploads.
          patch(item.id, { clipId: response.clipId });
          void queryClient.invalidateQueries({
            queryKey: storageUsageQueryKey,
          });
          invalidateClipCollections(queryClient);

          const upload = createTusClipUpload({
            file: item.file,
            response,
            title: item.title,
            onProgress: (uploaded, total) =>
              patch(item.id, {
                progress: total > 0 ? Math.round((uploaded / total) * 100) : 0,
              }),
            onSuccess: () => {
              uploads.current.delete(item.id);
              preparedClipIds.current.delete(item.id);
              patch(item.id, { status: "uploaded", progress: 100 });
              invalidateClipCollections(queryClient);
            },
            onError: (error) => {
              uploads.current.delete(item.id);
              void releasePreparedClip(item.id);
              patch(item.id, {
                status: "error",
                error: uploadErrorMessage(error),
              });
            },
          });
          uploads.current.set(item.id, upload);

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
          patch(item.id, { status: "uploading" });
          upload.start();
        } catch (error) {
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
    [patch, queryClient, releasePreparedClip],
  );

  // Keep a few uploads running at once; the rest wait their turn.
  useEffect(() => {
    if (paused) return;
    const active = items.filter(isActiveUpload).length;
    if (active >= MAX_ACTIVE_UPLOADS) return;
    items
      .filter((item) => item.status === "queued")
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

  // Leaving the site drops uploads still in this tab; processing on Bunny carries on regardless.
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
        progress: 0,
        clipId: null,
        error: null,
      }));
      setItems((current) => [...current, ...added]);
      return result.rejected;
    },
    [],
  );

  const pauseAll = useCallback(() => {
    setPaused(true);
    setItems((current) =>
      current.map((item) => {
        if (item.status !== "uploading") return item;
        uploads.current.get(item.id)?.abort();
        return { ...item, status: "paused" };
      }),
    );
  }, []);

  const resumeAll = useCallback(() => {
    setPaused(false);
    setItems((current) =>
      current.map((item) => {
        if (item.status !== "paused") return item;
        const upload = uploads.current.get(item.id);
        if (!upload) return { ...item, status: "queued" };
        upload.start();
        return { ...item, status: "uploading" };
      }),
    );
  }, []);

  const retry = useCallback(
    (id: string) =>
      setItems((current) =>
        current.map((item) =>
          item.id === id && item.status === "error"
            ? { ...item, status: "queued", error: null, clipId: null }
            : item,
        ),
      ),
    [],
  );

  const clearFinished = useCallback(
    () => setItems((current) => current.filter(isUnfinishedUpload)),
    [],
  );

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
