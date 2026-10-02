/**
 * Gives dev's copy of the database Bunny videos and collections of its own, so nothing dev does (deleting a clip,
 * deleting an account) can reach a video prod still serves. Run after prepare-dev-copy.sql, and before clips-dev
 * is pointed at the copy (see README.md):
 *
 *   DEV_DATABASE_CONNECTION_STRING="Host=...;Database=reelshelf_dev;..." bun scripts/dev-db/clone-bunny-videos.ts
 *
 * BunnyLibraryId and BunnyAccessKey come from the environment, as they do for the API. Each prod collection gets
 * a `Development-` twin in the same library. Each clip's video is downloaded from the CDN (the original file when
 * the library keeps it, otherwise the best HLS rendition remuxed by ffmpeg) and uploaded as a new video, then the
 * clip is pointed at it. Progress is kept in dev_copy_video, so the script can be run again after a failure and
 * picks up where it stopped. It refuses any database without the dev_copy table that only the prepare step makes.
 */
import { SQL } from "bun";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const BunnyStatusProcessing = 1;

function required(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    console.error(`Missing ${name}.`);
    process.exit(2);
  }
  return value;
}

const libraryId = required("BunnyLibraryId");
const accessKey = required("BunnyAccessKey");
const cdnHost =
  process.env.BUNNY_CDN_HOST?.trim() || "vz-cd8f9809-39a.b-cdn.net";
const collectionEnvironment =
  process.env.COLLECTION_ENVIRONMENT?.trim() || "Development";
const concurrency = Number(process.env.CLONE_CONCURRENCY ?? 4);
// Copies at most this many videos per run; set it to try the script on a few clips first.
const limit = Number(process.env.CLONE_LIMIT ?? Number.POSITIVE_INFINITY);

const db = connect(required("DEV_DATABASE_CONNECTION_STRING"));

/** Accepts a postgres:// URL or the Npgsql key=value form the API is configured with. */
function connect(connectionString: string): SQL {
  if (/^postgres(ql)?:\/\//.test(connectionString)) {
    return new SQL(connectionString);
  }

  const parts = new Map<string, string>();
  for (const pair of connectionString.split(";")) {
    const index = pair.indexOf("=");
    if (index > 0) {
      parts.set(
        pair.slice(0, index).trim().toLowerCase().replaceAll(" ", ""),
        pair.slice(index + 1).trim(),
      );
    }
  }

  const sslMode = parts.get("sslmode")?.toLowerCase();
  return new SQL({
    hostname: parts.get("host") ?? parts.get("server"),
    port: Number(parts.get("port") ?? 5432),
    database: parts.get("database"),
    username: parts.get("username") ?? parts.get("userid") ?? parts.get("user"),
    password: parts.get("password"),
    tls:
      sslMode === "require" ||
      sslMode === "verifyca" ||
      sslMode === "verifyfull",
  });
}

async function bunny(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("AccessKey", accessKey);
  headers.set("Accept", "application/json");
  return fetch(`https://video.bunnycdn.com/library/${libraryId}${path}`, {
    ...init,
    headers,
  });
}

async function bunnyJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await bunny(path, init);
  if (!response.ok) {
    throw new Error(
      `Bunny ${init.method ?? "GET"} ${path} returned ${response.status}: ${await response.text()}`,
    );
  }
  return (await response.json()) as T;
}

function jsonBody(body: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  };
}

async function cloneCollections(): Promise<void> {
  const pending: {
    clip_collection_id: string;
    owner_id: string;
    slug: string;
  }[] = await db`
    SELECT d.clip_collection_id, cc.owner_id, g.slug
    FROM dev_copy_collection d
    JOIN clip_collection cc ON cc.id = d.clip_collection_id
    JOIN game_category g ON g.id = cc.game_category_id
    WHERE d.dev_collection_id IS NULL`;

  console.log(`Collections to create: ${pending.length}`);
  for (const row of pending) {
    // Named the way BunnyService.CreateCollectionAsync names them.
    const name = `${collectionEnvironment}-${row.slug}-${row.owner_id}`;
    const collection = await bunnyJson<{ guid: string }>(
      "/collections",
      jsonBody({ name }),
    );
    await db.begin(async (tx) => {
      await tx`UPDATE dev_copy_collection SET dev_collection_id = ${collection.guid} WHERE clip_collection_id = ${row.clip_collection_id}`;
      await tx`UPDATE clip_collection SET collection_id = ${collection.guid} WHERE id = ${row.clip_collection_id}`;
    });
  }
}

type PendingVideo = {
  clip_id: string;
  prod_video_id: string;
  dev_video_id: string | null;
  title: string | null;
  collection_id: string | null;
};

async function cloneVideos(): Promise<number> {
  const pending: PendingVideo[] = await db`
    SELECT d.clip_id, d.prod_video_id, d.dev_video_id, c.title, cc.collection_id
    FROM dev_copy_video d
    JOIN clip c ON c.id = d.clip_id
    LEFT JOIN clip_collection cc ON cc.owner_id = c.owner_id AND cc.game_category_id = c.game_category_id
    WHERE d.state IN ('pending', 'created')
    ORDER BY c.created_at
    LIMIT ${Number.isFinite(limit) ? limit : null}`;

  console.log(`Videos to copy: ${pending.length}`);
  const workDir = await mkdtemp(join(tmpdir(), "reelshelf-dev-copy-"));
  let next = 0;
  let failed = 0;

  try {
    await Promise.all(
      Array.from({ length: concurrency }, async () => {
        while (next < pending.length) {
          const index = next++;
          const row = pending[index];
          try {
            const outcome = await cloneVideo(row, workDir);
            console.log(
              `[${index + 1}/${pending.length}] clip ${row.clip_id}: ${outcome}`,
            );
          } catch (error) {
            failed++;
            const message =
              error instanceof Error ? error.message : String(error);
            console.error(
              `[${index + 1}/${pending.length}] clip ${row.clip_id} failed: ${message}`,
            );
            await db`UPDATE dev_copy_video SET error = ${message}, updated_at = now() WHERE clip_id = ${row.clip_id}`;
          }
        }
      }),
    );
  } finally {
    await rm(workDir, { recursive: true, force: true });
  }

  return failed;
}

async function cloneVideo(row: PendingVideo, workDir: string): Promise<string> {
  const prodVideo = await bunny(`/videos/${row.prod_video_id}`);
  const prodStorage = prodVideo.ok
    ? ((await prodVideo.json()) as { storageSize: number }).storageSize
    : 0;
  if (prodVideo.status !== 404 && !prodVideo.ok) {
    throw new Error(`Bunny GET video returned ${prodVideo.status}`);
  }

  if (prodStorage === 0) {
    // Nothing to copy (never finished uploading, or already gone); drop the row so dev never refers to prod's id.
    await db.begin(async (tx) => {
      await tx`DELETE FROM clip WHERE id = ${row.clip_id} AND video_id = ${row.prod_video_id}`;
      await tx`UPDATE dev_copy_video SET state = 'dropped', error = NULL, updated_at = now() WHERE clip_id = ${row.clip_id}`;
    });
    return "dropped (prod video has nothing to copy)";
  }

  // A video left by an earlier failed attempt may hold a partial upload; start again with a fresh one.
  if (row.dev_video_id) {
    const removed = await bunny(`/videos/${row.dev_video_id}`, {
      method: "DELETE",
    });
    if (!removed.ok && removed.status !== 404) {
      throw new Error(
        `Bunny DELETE of earlier dev video returned ${removed.status}`,
      );
    }
  }

  const devVideo = await bunnyJson<{ guid: string }>(
    "/videos",
    jsonBody({
      title: row.title ?? "Untitled",
      collectionId: row.collection_id ?? undefined,
    }),
  );
  await db`UPDATE dev_copy_video SET dev_video_id = ${devVideo.guid}, state = 'created', updated_at = now() WHERE clip_id = ${row.clip_id}`;

  const file = join(workDir, `${row.prod_video_id}.mp4`);
  try {
    const source = await download(row.prod_video_id, file);
    const upload = await bunny(`/videos/${devVideo.guid}`, {
      method: "PUT",
      headers: { "Content-Type": "application/octet-stream" },
      body: Bun.file(file),
    });
    if (!upload.ok) {
      throw new Error(
        `Bunny PUT video returned ${upload.status}: ${await upload.text()}`,
      );
    }

    await db.begin(async (tx) => {
      // ClipStatusRefreshService picks up the dev video's length, thumbnail and size once Bunny has encoded it.
      await tx`
        UPDATE clip
        SET video_id = ${devVideo.guid}, video_status = ${BunnyStatusProcessing}, encode_progress = 0
        WHERE id = ${row.clip_id} AND video_id = ${row.prod_video_id}`;
      await tx`
        UPDATE dev_copy_video SET state = 'swapped', source = ${source}, error = NULL, updated_at = now()
        WHERE clip_id = ${row.clip_id}`;
    });
    return `copied from ${source}`;
  } finally {
    await rm(file, { force: true });
  }
}

/** The original upload when the library keeps originals, otherwise the highest HLS rendition remuxed to MP4. */
async function download(videoId: string, file: string): Promise<string> {
  const original = await fetch(`https://${cdnHost}/${videoId}/original`);
  if (original.ok) {
    await Bun.write(file, original);
    return "original";
  }

  const playlistUrl = `https://${cdnHost}/${videoId}/playlist.m3u8`;
  const playlist = await fetch(playlistUrl);
  if (!playlist.ok) {
    throw new Error(
      `No original (${original.status}) and no HLS playlist (${playlist.status})`,
    );
  }

  const variant = bestVariant(await playlist.text(), playlistUrl);
  const ffmpeg = Bun.spawn(
    [
      "ffmpeg",
      "-hide_banner",
      "-loglevel",
      "error",
      "-y",
      "-i",
      variant.url,
      "-c",
      "copy",
      "-bsf:a",
      "aac_adtstoasc",
      "-movflags",
      "+faststart",
      file,
    ],
    { stdout: "ignore", stderr: "pipe" },
  );
  const stderr = await new Response(ffmpeg.stderr).text();
  if ((await ffmpeg.exited) !== 0) {
    throw new Error(`ffmpeg failed: ${stderr.trim()}`);
  }
  return `hls ${variant.name}`;
}

function bestVariant(
  playlist: string,
  playlistUrl: string,
): { url: string; name: string } {
  const lines = playlist.split("\n").map((line) => line.trim());
  let best: { url: string; name: string; bandwidth: number } | null = null;
  for (let index = 0; index < lines.length - 1; index++) {
    const line = lines[index];
    const uri = lines[index + 1];
    if (!line.startsWith("#EXT-X-STREAM-INF") || !uri || uri.startsWith("#")) {
      continue;
    }
    const bandwidth = Number(/BANDWIDTH=(\d+)/.exec(line)?.[1] ?? 0);
    if (!best || bandwidth > best.bandwidth) {
      best = {
        url: new URL(uri, playlistUrl).toString(),
        name: uri,
        bandwidth,
      };
    }
  }
  // A media playlist rather than a master one: it is the only rendition.
  return best ?? { url: playlistUrl, name: "playlist.m3u8" };
}

const [{ prepared }]: { prepared: boolean }[] =
  await db`SELECT to_regclass('dev_copy') IS NOT NULL AS prepared`;
if (!prepared) {
  console.error(
    "This database has no dev_copy table. Run prepare-dev-copy.sql on the dev copy first; never run this on prod.",
  );
  process.exit(3);
}

await cloneCollections();
const failed = await cloneVideos();

const [{ remaining }]: { remaining: number }[] = await db`
  SELECT count(*)::int AS remaining FROM dev_copy_video WHERE state IN ('pending', 'created')`;
const [{ prod_references }]: { prod_references: number }[] = await db`
  SELECT count(*)::int AS prod_references FROM clip c JOIN dev_copy_video d ON d.prod_video_id = c.video_id`;
const [{ prod_collections }]: { prod_collections: number }[] = await db`
  SELECT count(*)::int AS prod_collections FROM clip_collection cc
  JOIN dev_copy_collection d ON d.prod_collection_id = cc.collection_id`;

console.log(`Failed this run: ${failed}. Still to copy: ${remaining}.`);
console.log(
  `Clips still on a prod video: ${prod_references}. Collections still prod's: ${prod_collections}.`,
);
await db.close();

if (remaining > 0 || prod_references > 0 || prod_collections > 0) {
  console.error(
    "Not finished: run the script again. Don't point clips-dev at this database until every count is 0.",
  );
  process.exit(1);
}
console.log(
  "Done: the dev copy no longer refers to any prod video or collection.",
);
