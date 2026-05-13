import type { BulkUploadFileInput } from "@/hooks/bulkUploadQueue";

type DroppedFileSystemEntry = {
  fullPath: string;
  isDirectory: boolean;
  isFile: boolean;
  name: string;
};

type DroppedFileSystemFileEntry = DroppedFileSystemEntry & {
  file: (
    successCallback: (file: File) => void,
    errorCallback?: (error: DOMException) => void,
  ) => void;
};

type DroppedFileSystemDirectoryEntry = DroppedFileSystemEntry & {
  createReader: () => {
    readEntries: (
      successCallback: (entries: DroppedFileSystemEntry[]) => void,
      errorCallback?: (error: DOMException) => void,
    ) => void;
  };
};

type DataTransferItemWithEntry = DataTransferItem & {
  webkitGetAsEntry?: () => DroppedFileSystemEntry | null;
};

export function dataTransferHasFiles(dataTransfer: DataTransfer) {
  return (
    Array.from(dataTransfer.types).includes("Files") ||
    Array.from(dataTransfer.items).some((item) => item.kind === "file")
  );
}

export async function bulkUploadInputsFromDataTransfer(
  dataTransfer: DataTransfer,
): Promise<BulkUploadFileInput[]> {
  const entries: DroppedFileSystemEntry[] = [];
  for (const item of Array.from(dataTransfer.items)) {
    const entry = (item as DataTransferItemWithEntry).webkitGetAsEntry?.();
    if (entry) entries.push(entry);
  }

  if (!entries.length) {
    return Array.from(dataTransfer.files);
  }

  const files = await Promise.all(
    entries.map((entry) => filesFromEntry(entry)),
  );
  return files.flat();
}

async function filesFromEntry(
  entry: DroppedFileSystemEntry,
): Promise<BulkUploadFileInput[]> {
  if (entry.isFile) {
    const file = await fileFromEntry(entry as DroppedFileSystemFileEntry);
    return [
      {
        file,
        sourcePath: trimEntryPath(entry.fullPath || entry.name),
      },
    ];
  }

  if (!entry.isDirectory) return [];

  const childEntries = await readDirectoryEntries(
    entry as DroppedFileSystemDirectoryEntry,
  );
  const childFiles = await Promise.all(
    childEntries.map((childEntry) => filesFromEntry(childEntry)),
  );
  return childFiles.flat();
}

function fileFromEntry(entry: DroppedFileSystemFileEntry) {
  return new Promise<File>((resolve, reject) => {
    entry.file(resolve, reject);
  });
}

function readDirectoryEntries(entry: DroppedFileSystemDirectoryEntry) {
  const reader = entry.createReader();
  const entries: DroppedFileSystemEntry[] = [];

  return new Promise<DroppedFileSystemEntry[]>((resolve, reject) => {
    function readNextBatch() {
      reader.readEntries((batch) => {
        if (!batch.length) {
          resolve(entries);
          return;
        }

        entries.push(...batch);
        readNextBatch();
      }, reject);
    }

    readNextBatch();
  });
}

function trimEntryPath(path: string) {
  return path.replace(/^\/+/, "");
}
