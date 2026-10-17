/**
 * Every media URL the frontend builds for a clip's video. Nothing else names the video host, so moving off it
 * (or having the API hand these out) touches only this module.
 */

/** Bunny's video CDN, which serves each video's HLS playlist and thumbnails. */
export const videoCdnBaseUrl = "https://vz-cd8f9809-39a.b-cdn.net";

/**
 * Discord's CSP only lets the Activity load from its proxy origin, so the video CDN reaches it through the
 * `/bunny-cdn` URL Mapping (→ {@link videoCdnBaseUrl}) set on the Discord application.
 */
export const activityVideoCdnBaseUrl = "/bunny-cdn";

/** The HLS playlist; it uses relative paths, so segments come from the same base. */
export function videoPlaylistUrl(
  videoId: string,
  cdnBaseUrl = videoCdnBaseUrl,
) {
  return `${cdnBaseUrl}/${videoId}/playlist.m3u8`;
}

export function videoThumbnailUrl(
  videoId: string,
  cdnBaseUrl = videoCdnBaseUrl,
) {
  return `${cdnBaseUrl}/${videoId}/thumbnail.jpg`;
}

export function videoEmbedUrl(
  videoLibraryId: number | string,
  videoId: string,
  autoplay = false,
) {
  return `https://player.mediadelivery.net/embed/${videoLibraryId}/${videoId}?autoplay=${autoplay}`;
}
