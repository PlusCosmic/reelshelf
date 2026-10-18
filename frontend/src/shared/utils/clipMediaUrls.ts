import type { PlaybackSource } from "@/api-client";

/**
 * The one place the frontend builds a clip's media URLs itself. Everywhere else uses the URLs the API hands out
 * (`clip.video.thumbnailUrl`, `sources`, and the same fields on other responses), so the video host is only
 * named by the API.
 *
 * The exception is the Discord Activity: Discord's CSP only lets it load from its proxy origin, so each media
 * origin reaches it through a URL Mapping set on the Discord application, and these paths have to be built here
 * against those mappings rather than taken from the API. The API hands the Activity each clip's host, its folder
 * under that host's origin and the stream's type (`ActivityMedia`).
 */

/** Bunny Stream's pull zone, through the `/bunny-cdn` URL Mapping. */
export const activityVideoCdnBaseUrl = "/bunny-cdn";

/** Our own pipeline's media domain (R2 behind the Cloudflare CDN), through the `/media` URL Mapping. */
export const activityMediaBaseUrl = "/media";

/** Where the Activity finds a clip's video: the API's `ActivityMedia`. */
export type ActivityMediaRef = {
  /** The clip's video host (`bunny` or `r2`). */
  host: string;
  /** The video's folder under its host's media origin. */
  path: string;
  /** The stream's MIME type, with codecs where known; null until the clip is ready and has a stream. */
  type: string | null;
  /** Whether the clip's thumbnail exists yet. An R2 clip's appears only once its worker has made it. */
  hasThumbnail: boolean;
};

/**
 * What the Activity's player plays, through the host's mapping. Bunny's HLS playlist uses relative paths, so its
 * segments come the same way. Null for a host the Activity has no mapping for, or a clip with no stream yet.
 */
export function activityStreamSource(
  media: Pick<ActivityMediaRef, "host" | "path" | "type">,
): PlaybackSource | null {
  if (media.type === null) return null;
  switch (media.host) {
    case "bunny":
      return {
        url: `${activityVideoCdnBaseUrl}/${media.path}/playlist.m3u8`,
        type: media.type,
      };
    case "r2":
      return {
        url: `${activityMediaBaseUrl}/${media.path}/av1.mp4`,
        type: media.type,
      };
    default:
      return null;
  }
}

/**
 * The clip's main thumbnail through the host's mapping; both hosts call it `thumbnail.jpg`. Null until it exists,
 * so a request never caches a 404 for it.
 */
export function activityThumbnailUrl(media: ActivityMediaRef): string | null {
  if (!media.hasThumbnail) return null;
  switch (media.host) {
    case "bunny":
      return `${activityVideoCdnBaseUrl}/${media.path}/thumbnail.jpg`;
    case "r2":
      return `${activityMediaBaseUrl}/${media.path}/thumbnail.jpg`;
    default:
      return null;
  }
}
