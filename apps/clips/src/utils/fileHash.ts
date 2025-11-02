import CryptoJS from 'crypto-js';

/**
 * Calculate MD5 hash of a file
 * @param file - The File object to hash
 * @returns Promise that resolves to the MD5 hash as a hex string
 */
export async function calculateFileMD5(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = (event) => {
      try {
        const arrayBuffer = event.target?.result as ArrayBuffer;

        // Convert ArrayBuffer to WordArray for CryptoJS
        const wordArray = CryptoJS.lib.WordArray.create(arrayBuffer);

        // Calculate MD5 hash
        const hash = CryptoJS.MD5(wordArray);

        // Convert to hex string
        const hashHex = hash.toString(CryptoJS.enc.Hex);

        resolve(hashHex);
      } catch (error) {
        reject(error);
      }
    };

    reader.onerror = () => {
      reject(new Error('Failed to read file'));
    };

    // Read the file as ArrayBuffer
    reader.readAsArrayBuffer(file);
  });
}
