export function formatDuration(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function formatDate(dateString: string): string {
  const date = new Date(dateString);
  const now = new Date();
  const diffTime = Math.abs(now.getTime() - date.getTime());
  const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

  if (diffDays === 0) return 'Today';
  if (diffDays === 1) return 'Yesterday';
  if (diffDays < 7) return `${diffDays} days ago`;
  if (diffDays < 30) return `${Math.floor(diffDays / 7)} weeks ago`;
  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

/**
 * Check if a video clip is still processing
 * Status: 0-2 = processing, 3-4 = ready
 */
export function isClipProcessing(status: number): boolean {
  return status >= 0 && status <= 2;
}

/**
 * Get processing status message based on video status
 */
export function getProcessingStatusMessage(status: number, encodeProgress: number): string {
  switch (status) {
    case 0:
      return 'Queued for processing...';
    case 1:
      return `Encoding... ${Math.round(encodeProgress)}%`;
    case 2:
      return 'Finalizing...';
    case 3:
    case 4:
      return 'Ready';
    default:
      return 'Unknown status';
  }
}
