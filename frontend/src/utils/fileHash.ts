import CryptoJS from "crypto-js";

/**
 * Calculate MD5 hash of a file using chunked reading to avoid memory issues with large files
 * @param file - The File object to hash
 * @param chunkSize - Size of each chunk to read (default 2MB)
 * @returns Promise that resolves to the MD5 hash as a hex string
 */
export async function calculateFileMD5(
  file: File,
  chunkSize = 2 * 1024 * 1024,
): Promise<string> {
  return new Promise((resolve, reject) => {
    const md5 = CryptoJS.algo.MD5.create();
    const reader = new FileReader();
    let offset = 0;

    const readNextChunk = () => {
      if (offset >= file.size) {
        // All chunks processed, finalize the hash
        const hash = md5.finalize();
        const hashHex = hash.toString(CryptoJS.enc.Hex);
        resolve(hashHex);
        return;
      }

      const chunk = file.slice(offset, offset + chunkSize);
      reader.readAsArrayBuffer(chunk);
    };

    reader.onload = (event) => {
      try {
        const arrayBuffer = event.target?.result as ArrayBuffer;

        // Convert ArrayBuffer to WordArray for CryptoJS
        const wordArray = CryptoJS.lib.WordArray.create(arrayBuffer);

        // Update the hash with this chunk
        md5.update(wordArray);

        // Move to next chunk
        offset += chunkSize;
        readNextChunk();
      } catch (error) {
        reject(error);
      }
    };

    reader.onerror = () => {
      reject(new Error("Failed to read file"));
    };

    // Start reading the first chunk
    readNextChunk();
  });
}
