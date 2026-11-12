# Storage preflight

`Reelshelf.StorageCheck` runs the API's and the transcode worker's own storage code against a real bucket, evidence
bucket and CDN, and says what works. It is for the moment before the API is pointed at a new store (the move from R2
to S3 and CloudFront): nothing else exercises that code against the real thing until users do.

It calls the same classes production does, not a re-implementation: `R2Options.FromConfiguration`, `R2Storage`
(multipart starts, part URLs, `ListParts`, completion, `UrlsFor`, `UploadAsync`, `PutAsync`, reads, lists, copies,
deletes, aborts), `R2Downloads` and `DownloadFileName`, `ClipExportBuilder` with `ClipExportVideoSource` and
`R2ExportStorage`, `R2GameArtStore`, `R2EvidenceQuarantine`, `R2EvidenceStorage`, `CloudFrontCdnPurger`,
`FfprobeUploadProbe` (when `ffprobe` is on `PATH`) and the worker's `MediaTransfer`. The browser's and the desktop
uploader's part uploads, CORS and the CDN are plain HTTP requests shaped like theirs.

## Safety

- Every key it writes starts with a fresh random GUID (a clip, evidence, export account or game category id), printed
  first, so it can't overwrite or delete anything else. Real ids are random GUIDs too. It never assumes the bucket is
  empty or full: run it on an empty dev bucket or on a prod bucket holding the whole library.
- It always cleans up, after a failure and after Ctrl-C or `SIGTERM` (further signals are ignored until cleanup is
  done; it gives up after 3 minutes): it aborts any multipart upload open on its keys, deletes its keys and folders in
  both buckets, then lists them again. If anything is left it lists the keys and exits 3.
- It reads back only its own objects (about 50 MiB moves in all; a run takes well under a couple of minutes on a
  decent connection) and lists only its own prefixes.
- It creates at most one CloudFront invalidation, for its own clip's paths, and says so.
- Only the settings it needs are read from the environment, so `infisical run` can supply them alongside every other
  secret and the live `R2__*` settings, which are ignored. The key is never accepted on the command line, and no
  secret or presigned signature is ever printed.

## Running it

From the repository root, with the .NET 10 SDK. Settings are read from the environment, then `--env-file`, then the
command line (later wins); `--help` lists every option.

| Setting                                            | From                                                                  |
| -------------------------------------------------- | --------------------------------------------------------------------- |
| `Storage__Region`, `Storage__Bucket`               | infrastructure's `aws/storage`: `terraform output clips_settings`     |
| `Storage__MediaBaseUrl`                            | the same output: the media hostname, `https://media...`               |
| `Storage__AccessKeyId`, `Storage__SecretAccessKey` | the env file `aws/storage/secrets.sh` writes, or `infisical run`      |
| `Moderation__EvidenceBucket` (optional)            | the evidence bucket's name, if the instance will set it               |
| `Moderation__CloudFront__DistributionId`           | `terraform output distribution_ids` (optional; enables the purge)     |
| `--site-origin`                                    | the site whose browsers upload: `https://reelshelf.app` (prod)        |
| `--media-connect-host`                             | `terraform output distribution_domains`, while DNS still points at R2 |

`--media-connect-host` connects to the distribution's `*.cloudfront.net` name while every request still carries the
media hostname (TLS SNI, certificate validation and `Host`), as browsers will once DNS moves. The media hostname must
be attached to the distribution (`attach_media_hostname`). After the DNS change, leave it out.

Run it against dev first, then prod. In a POSIX shell:

```sh
export Storage__Region=eu-north-1 Storage__Bucket=<dev media bucket> \
  Storage__MediaBaseUrl=https://<dev media hostname> \
  Moderation__EvidenceBucket=<dev evidence bucket> Moderation__CloudFront__DistributionId=<dev distribution id>
dotnet run --project tools/Reelshelf.StorageCheck -- --env-file <dev key env file> \
  --site-origin https://<dev site> --media-connect-host <dev distribution domain>
```

In fish:

```fish
set -x Storage__Region eu-north-1; set -x Storage__Bucket <prod media bucket>
set -x Storage__MediaBaseUrl https://<prod media hostname>
set -x Moderation__EvidenceBucket <prod evidence bucket>; set -x Moderation__CloudFront__DistributionId <prod distribution id>
dotnet run --project tools/Reelshelf.StorageCheck -- --env-file <prod key env file> \
    --site-origin https://reelshelf.app --media-connect-host <prod distribution domain>
```

With Infisical supplying the key instead of an env file, put the same command after `infisical run --env=<env> --`.

## Reading the output

Each check prints `PASS`, `FAIL`, `SKIP` or `NOTE` with its reason; a failure carries the HTTP status and S3 or
CloudFront error code. The sections follow a clip's life: missing keys, the browser upload (CORS preflight, part PUTs
and their exposed `ETag`, the desktop uploader's PUT, listing parts, completing twice), the upload probe's ranged
reads, the worker's download and uploads with their signed headers (and the refusal of any other `Content-Type` or
`Cache-Control`), the download link's `Content-Disposition`, the server's own uploads (one PUT and a multipart upload
from a stream of unknown length), an export zip streamed through a pipe, a game art copy, the quarantine copy, the
evidence bucket, an abandoned upload's abort, the CDN (served, ranges, `immutable`, `uploads/`, `quarantine/` and
`exports/` refused, a miss not cached, HTTP to HTTPS) and the purge, which caches a file at the edge, deletes it from
the bucket and waits (up to `--purge-wait-seconds`, or not with `--no-purge-wait`) for the edge to stop serving it.
When the browser's upload fails, the source goes in server side so the later sections still run.

The summary lists every check, what was skipped and why, and the cleanup's result. Exit codes: 0 every check that ran
passed and cleanup was verified; 1 a check failed; 2 a usage error; 3 cleanup couldn't be verified; 130 interrupted.
A `NOTE` never fails the run: the evidence bucket's key can't list open multipart uploads, for example, so cleanup
can't look for any there (the API's uploads there abort themselves when they fail).

If cleanup reports keys left, delete exactly those (or the run's prefixes printed at the top) with an admin
credential, since the copy into the new bucket refuses to run over objects it didn't write.
