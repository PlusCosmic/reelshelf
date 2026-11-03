import { createMD5 } from "hash-wasm";

const CHUNK_SIZE = 8 * 1024 * 1024;

/**
 * The file's MD5 as lowercase hex, read in chunks so a large clip never sits in memory whole. hash-wasm's
 * WebAssembly MD5 runs at several hundred MB/s; `hashFileMd5` runs this in a worker so the page stays responsive.
 */
export async function calculateFileMD5(
  file: Blob,
  chunkSize = CHUNK_SIZE,
): Promise<string> {
  const md5 = await createMD5();
  md5.init();
  for (let offset = 0; offset < file.size; offset += chunkSize) {
    const chunk = await file.slice(offset, offset + chunkSize).arrayBuffer();
    md5.update(new Uint8Array(chunk));
  }
  return md5.digest("hex");
}

type WorkerReply = { hash: string } | { error: string };

/**
 * Hashes the file in a Web Worker of its own, so several files hash on separate cores and the page stays
 * responsive; falls back to the page's own thread where workers aren't available (tests, old browsers).
 */
export function hashFileMd5(file: File): Promise<string> {
  if (typeof Worker === "undefined") return calculateFileMD5(file);

  let worker: Worker;
  try {
    worker = new Worker(new URL("./md5.worker.ts", import.meta.url), {
      type: "module",
    });
  } catch {
    return calculateFileMD5(file);
  }

  return new Promise<string>((resolve, reject) => {
    worker.onmessage = (event: MessageEvent<WorkerReply>) => {
      worker.terminate();
      if ("hash" in event.data) resolve(event.data.hash);
      else reject(new Error(event.data.error));
    };
    worker.onerror = (event) => {
      event.preventDefault();
      worker.terminate();
      // A worker that couldn't load leaves the job to the page's own thread.
      calculateFileMD5(file).then(resolve, reject);
    };
    worker.postMessage(file);
  });
}
