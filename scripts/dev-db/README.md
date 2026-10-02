# Splitting dev from prod's database

Until October 2026, `clips-dev` used the prod database. This is the one-off runbook that gave dev a copy of its own,
with Bunny videos and collections of its own, so destructive flows such as account deletion can be tried on dev.
Prod is only ever read from.

The copy keeps real accounts, emails and clips. It drops work prod still owes (queued legend detection and
transcription runs, accounts mid-deletion) and stored Twitch tokens, which dev can't decrypt anyway.

Bunny keeps one library: every prod collection gets a `Development-` twin and every clip's video is uploaded again
into it, so the copy doubles the library's storage and encodes each clip once more. Dev's own uploads already go
into `Development-` collections. The library's webhook still points at prod, which ignores videos it doesn't know;
dev learns when encoding finishes from `ClipStatusRefreshService` polling.

## 1. Create the dev database

On the Postgres server, as a superuser:

```sql
CREATE ROLE reelshelf_dev LOGIN PASSWORD '<new password>';
CREATE DATABASE reelshelf_dev OWNER reelshelf_dev;
```

The name must contain `dev`; the prepare script refuses any other.

## 2. Copy prod into it

From the server, with a client image of the same major version as the server (`SELECT version();`):

```sh
docker run --rm --network dokploy-network \
  -e PROD_URL='postgresql://<prod user>:<password>@<host>:5432/<prod db>' \
  -e DEV_URL='postgresql://reelshelf_dev:<password>@<host>:5432/reelshelf_dev' \
  -v "$HOME/dev/reelshelf/scripts/dev-db:/scripts:ro" \
  postgres:<major> sh -c '
    pg_dump --format=custom "$PROD_URL" \
      | pg_restore --no-owner --no-privileges --exit-on-error --dbname="$DEV_URL" \
    && psql "$DEV_URL" -v ON_ERROR_STOP=1 -f /scripts/prepare-dev-copy.sql'
```

Restoring as `reelshelf_dev` with `--no-owner` makes it the owner of everything. The Evolve `changelog` comes along,
so the copy is at prod's migration version.

## 3. Give the copy its own Bunny videos

`clips-dev` has the Bunny settings, ffmpeg and this checkout, so run it there. It keeps using prod's database until
step 4; the script only connects to the database you pass:

```sh
docker exec -e DEV_DATABASE_CONNECTION_STRING='Host=<host>;Port=5432;Database=reelshelf_dev;Username=reelshelf_dev;Password=<password>' \
  clips-dev bun scripts/dev-db/clone-bunny-videos.ts
```

It prints a line per clip and finishes with the counts of clips and collections still pointing at prod. Run it
again until it reports `Done`; each run carries on from `dev_copy_video`. Clips whose prod video has nothing to copy
are deleted from the copy. `CLONE_CONCURRENCY` (default 4) sets how many clips copy at once. Pass `-e CLONE_LIMIT=5`
on the first run to copy only a few, and check those encode and play in the Bunny dashboard before doing the rest.

Don't go on to step 4 until it reports `Done`: until then, a clip in the copy can still point at prod's video.

## 4. Point clips-dev at the copy

Merge the infrastructure change that reads `ClipsDevDatabaseConnectionString` in `services/clips-dev.yml`, set it
in Dokploy to the step 3 connection string, and redeploy `clips-dev`. Dev now runs its own migrations when it
starts, and runs the account deletion job on its own data.

## Afterwards

`dev_copy`, `dev_copy_collection` and `dev_copy_video` stay in the dev database as a record of what was copied from
where. Nothing reads them once the copy is done.
