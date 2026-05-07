import { apiConfig } from "../config/apiConfig";

/**
 * Trigger a video download in the browser
 * @param videoId - The ID of the video to download
 */
export async function downloadVideo(videoId: string): Promise<void> {
  // Construct the download URL
  const downloadUrl = `${apiConfig.baseUrl}/api/ffmpeg/download/${encodeURIComponent(videoId)}`;

  // Create a temporary anchor element to trigger the download
  const link = document.createElement('a');
  link.href = downloadUrl;
  link.setAttribute('download', ''); // This suggests to download rather than navigate

  // Trigger the download
  document.body.appendChild(link);
  link.click();

  // Clean up
  document.body.removeChild(link);
}
