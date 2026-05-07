/**
 * Formats a byte count into a human-readable file size
 * @param bytes - The size in bytes
 * @returns Formatted string (e.g., "1.5 KB", "2.3 MB")
 */
export function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}

/**
 * Extracts the file extension from a filename or path
 * @param filename - The filename or path to extract extension from
 * @returns The extension including the dot (e.g., ".json") or empty string if none
 */
export function getFileExtension(filename: string): string {
  const basename = filename.split("/").pop() || filename;
  const lastDot = basename.lastIndexOf(".");
  return lastDot > 0 ? basename.substring(lastDot) : "";
}
