/**
 * The one place the frontend builds a clip's media URLs itself. Everywhere else uses the URLs the API hands out
 * (`clip.video.thumbnailUrl`, `playbackUrl`, and the same fields on other responses), so the video host is only
 * named by the API.
 *
 * The exception is the Discord Activity: Discord's CSP only lets it load from its proxy origin, so the video CDN
 * reaches it through the `/bunny-cdn` URL Mapping set on the Discord application, and these paths have to be
 * built here against that mapping rather than taken from the API.
 */
export const activityVideoCdnBaseUrl = "/bunny-cdn";

/** The HLS playlist through the Activity's mapping; it uses relative paths, so segments come the same way. */
export function activityVideoPlaylistUrl(videoId: string) {
  return `${activityVideoCdnBaseUrl}/${videoId}/playlist.m3u8`;
}

export function activityVideoThumbnailUrl(videoId: string) {
  return `${activityVideoCdnBaseUrl}/${videoId}/thumbnail.jpg`;
}
