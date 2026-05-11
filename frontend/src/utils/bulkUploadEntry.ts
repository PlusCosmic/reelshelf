import type { BulkUploadFileInput } from "@/hooks/bulkUploadQueue";

export type PendingBulkUploadEntry = {
  files: BulkUploadFileInput[];
  fallbackCategoryId?: string | null;
  source: "library" | "game";
};

let pendingBulkUploadEntry: PendingBulkUploadEntry | null = null;

export function setPendingBulkUploadEntry(entry: PendingBulkUploadEntry) {
  pendingBulkUploadEntry = {
    ...entry,
    files: [...entry.files],
  };
}

export function consumePendingBulkUploadEntry() {
  const entry = pendingBulkUploadEntry;
  pendingBulkUploadEntry = null;
  return entry;
}
