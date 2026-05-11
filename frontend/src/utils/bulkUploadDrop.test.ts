import { describe, expect, it } from "vitest";
import { bulkUploadInputsFromDataTransfer } from "./bulkUploadDrop";

describe("bulkUploadInputsFromDataTransfer", () => {
  it("preserves folder source paths from dropped directory entries", async () => {
    const file = new File(["clip"], "clip.mp4", { type: "video/mp4" });
    const directoryEntry = directory("Apex Legends", [
      fileEntry("/Apex Legends/session/clip.mp4", file),
    ]);
    const dataTransfer = {
      files: [] as unknown as FileList,
      items: [
        {
          kind: "file",
          webkitGetAsEntry: () => directoryEntry,
        },
      ],
    } as unknown as DataTransfer;

    await expect(
      bulkUploadInputsFromDataTransfer(dataTransfer),
    ).resolves.toEqual([
      {
        file,
        sourcePath: "Apex Legends/session/clip.mp4",
      },
    ]);
  });
});

function fileEntry(fullPath: string, file: File) {
  return {
    fullPath,
    isDirectory: false,
    isFile: true,
    name: file.name,
    file: (resolve: (file: File) => void) => resolve(file),
  };
}

function directory(name: string, entries: ReturnType<typeof fileEntry>[]) {
  let read = false;

  return {
    fullPath: `/${name}`,
    isDirectory: true,
    isFile: false,
    name,
    createReader: () => ({
      readEntries: (
        resolve: (entries: ReturnType<typeof fileEntry>[]) => void,
      ) => {
        if (read) {
          resolve([]);
          return;
        }

        read = true;
        resolve(entries);
      },
    }),
  };
}
