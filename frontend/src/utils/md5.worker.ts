import { calculateFileMD5 } from "./fileHash";

/** Hashes one file posted to it and answers `{ hash }` or `{ error }`. */
self.onmessage = async (event: MessageEvent<File>) => {
  try {
    self.postMessage({ hash: await calculateFileMD5(event.data) });
  } catch (error) {
    self.postMessage({
      error: error instanceof Error ? error.message : String(error),
    });
  }
};
