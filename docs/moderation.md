# Reports, takedowns and removals

Anyone watching a share link can report the clip. Admins deal with reports at `/admin/reports`. The code is in
`api/Moderation/`; the tables are `content_report` (V51) and `clip_evidence` (V52).

## What each decision does

| Report reason                          | Decision  | What happens                                                                                                                                                                                                                                                                                                      |
| -------------------------------------- | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Any                                    | Dismiss   | The report is closed. The clip is left alone.                                                                                                                                                                                                                                                                     |
| Anything but child safety or terrorism | Take down | Sharing is blocked (`clip.sharing_blocked_at`), the clip's share links are revoked, and every open report on it is closed but child-safety or terrorism ones, which stay open for removal. The owner is emailed that the clip broke the terms, without the rule or the reporter. The clip stays in their library. |
| Child safety or terrorism              | Remove    | See below. The owner isn't emailed.                                                                                                                                                                                                                                                                               |

A child-safety or terrorism report hides the share link the moment it is filed, and every Admin with an email
address is mailed. While it is open the clip is held as possible evidence: the share link stays hidden, the owner
can't delete the clip (the over-limit sweep skips it too), and the owner can't delete their account. Both refusals
say only that it can't be done right now.

An account that asked to be deleted before the report was filed is held too: each pass of the account deletion job
(`AccountDeletionService.Process`, checked again before each batch of 20 videos) still cancels its Stripe
subscriptions but deletes no video, clip row, collection or export while `IAccountDeletionStore.IsHeldForReview` is
true, and logs a warning that the deletion waits on a review. The account stays held until the report is dismissed, or
the clip is removed and the evidence processor has copied its video (`clip_evidence.files` is set); then the next pass
deletes the rest. `DeleteClips` also leaves out a clip under an open urgent report, should one be filed mid-batch.
Removing works for such an account, but it isn't suspended, since it is already being deleted.

### Removing a clip

One transaction (`ContentReportStatements.Remove`):

1. snapshots the clip row (with its game's name) and its owner (username, email and every linked sign-in) into a new
   `clip_evidence` record, kept for `Moderation:EvidenceRetention` (365 days);
2. suspends the owner (`app_user.suspended_at`, recorded in `account_access_change`), unless they are an Admin;
3. closes every open report on the clip as `removed`;
4. deletes the clip row, so nothing in Reelshelf lists or plays it from then on.

`ClipEvidenceProcessor` (run by `ClipEvidenceBackgroundService`) then works through four steps. The law requires the
content to be kept (see [Legal basis](#legal-basis)), so nothing of it is deleted until the evidence copy exists, but
it comes off public access first:

1. **Quarantine** (done once `cdn_purge` is set). Every object of the clip in the media bucket, everything under
   `clips/{clipId}/` and `uploads/{clipId}` (an open browser upload into it is aborted first), is copied server side
   to `quarantine/{evidenceId}/{its key}` in the same bucket, and only then are the originals deleted. Then every
   output URL of versions 1 to `video_version + 1` is purged from the media domain's cache (outputs are cached
   `immutable` for a year, so deleting them from R2 isn't enough). This uses the media bucket's own credentials, so
   it works whatever state the evidence bucket is in. A key already quarantined is never overwritten.
2. **Preserve** (`files`). `{evidenceId}/record.json` (the snapshots and the decision) and the video go to the
   evidence bucket: an R2 clip's current `av1.mp4` read from the quarantine (or, if it never finished encoding, its
   uploaded source as `{evidenceId}/source`), or a Bunny clip's HLS stream remuxed to MP4 as an export reads it, as
   `{evidenceId}/video.mp4`.
3. **Delete** (`media_deleted_at`). Only once the copy exists: the video is deleted at its host, with every R2
   version and any Bunny copy, and `quarantine/{evidenceId}/` is deleted. The record is then `preserved`.
4. **Sweep** (`swept_at`), after `Moderation:SweepAfter` (3 hours). Anything a transcode worker uploaded into
   `clips/{clipId}/` with presigned URLs it held at removal is quarantined (or, once the record is preserved,
   deleted), and the URLs purged again.

A **Bunny** clip can't be quarantined without the same download preserving does, so its video stays at Bunny, where
its URL still plays, until the copy is made; only its R2 objects, if a migration left any, are quarantined. A
failing copy of a Bunny clip is retried like any other and gets the same alerts.

Each step is recorded as it finishes and is safe to repeat, so a failed attempt resumes where it stopped. Steps are
written only under the claim that ran them (`claimed_at`): a processor whose claim went stale (`EvidenceStaleAfter`)
and was taken over writes nothing more.

After the retention, the evidence files and record are deleted, only once the record is `preserved`: a record whose
evidence copy doesn't exist yet is never deleted.

### Statuses

| `status`    | Meaning                                                                                                             |
| ----------- | ------------------------------------------------------------------------------------------------------------------- |
| `pending`   | The copy isn't made yet; being worked on. An R2 clip is quarantined from the first attempt on.                      |
| `failed`    | Needs attention: still no copy after `EvidenceMaxAttempts`. Still retried, and nothing is deleted; see below.       |
| `preserved` | The copy is in the evidence bucket, and the video is gone from its host and the quarantine. Kept for the retention. |

### Suspension

A suspended account can't sign in (the sign-in page says it's suspended), any session it had stops working at once,
its share links stop working, and it joins the Discord Activity as a guest. Its clips are kept. Admins suspend
accounts and lift suspensions at `/admin/accounts`, with a reason that is recorded; Admins can't be suspended, and an
admin can't suspend themselves.

## Setup

1. **Create the evidence bucket**, one per environment (e.g. `reelshelf-evidence-prod`), in the same Cloudflare
   account as the media bucket. Give it **no** public access: no custom domain and no `r2.dev` URL. A lifecycle rule
   isn't needed; the API deletes evidence after its retention.
2. **Let the API's R2 token reach it.** The API uses the same `R2:AccessKeyId`/`R2:SecretAccessKey` for both
   buckets, so a token scoped to specific buckets must include the evidence bucket with Object Read & Write.
3. **Set `Moderation__EvidenceBucket`** to the bucket's name. Without it the queue works, but "Remove clip" answers
   409 and nothing is removed.
4. **Set up the cache purge.** Create a Cloudflare API token with only the media domain zone's **Cache Purge**
   permission, and set `Moderation__Cloudflare__ZoneId` and `Moderation__Cloudflare__ApiToken`. Without them,
   removal still deletes the files but records `cdn_purge = 'not_configured'`, and the admin page tells you to purge
   the clip's URLs by hand.
5. **Block `/quarantine/*` on the media domain.** Quarantine keys are never handed out and carry a random evidence
   id, but the media domain serves the whole media bucket, so as defence in depth add a WAF custom rule on the media
   domain's zone: `(http.host eq "<media domain>" and starts_with(http.request.uri.path, "/quarantine/"))` →
   **Block**. It is set once, by hand for now; it still needs adding to the infrastructure repo's Cloudflare config.
   Check it by requesting any `/quarantine/...` URL on the media domain: it must answer Cloudflare's 403, not R2's 404. The media bucket's lifecycle rules must leave `quarantine/` alone (`docs/video-pipeline/r2-bucket.md`).

Other settings: `Moderation:ContactEmail` (where owners dispute a takedown), `Moderation:ReportsPerMinute` (5),
`EvidenceMaxAttempts` (5), `EvidenceRetryAfter` (5 minutes), `EvidenceMaxRetryAfter` (1 hour), `EvidenceStaleAfter`
(30 minutes) and `SweepAfter` (3 hours, which must stay above `Transcoding:PresignLifetime`).

## When an evidence copy fails

Nothing of a removed clip is ever deleted before the evidence copy exists, and preserving is never given up on.

While a record is `pending`, attempts are `EvidenceRetryAfter` apart and the admin page shows the error it is
retrying after. After `EvidenceMaxAttempts` the record is `failed`, which means **needs attention**, not given up: the
API logs it at Error, the admin page says the copy still isn't made, and it is retried with a back-off that doubles
from `EvidenceRetryAfter` up to `EvidenceMaxRetryAfter` (1 hour) for as long as it takes. Meanwhile:

- an **R2** clip is in the quarantine, off public access, and its URLs are purged;
- a **Bunny** clip's video is still at Bunny, and still plays from its Bunny URL, since nothing else holds a copy.
  This is the case to fix first.

Fix the cause (usually the evidence bucket or the token's access to it, or Bunny for a Bunny clip). The next attempt
picks it up, resuming from the step it stopped at; to retry at once rather than wait out the back-off:

```sql
UPDATE clip_evidence SET available_at = now() WHERE id = '<evidence id>';
```

Once the copy is made, the record goes to `preserved` and the error clears. A record whose copy doesn't exist yet is
never deleted at the end of its retention.

## Legal basis

Why removed child-safety and terrorism content is kept, privately, rather than deleted:

- **UK, Online Safety Act 2023 s66** (the duty to report detected CSEA content to the NCA, in force since 7 April 2026) and the **Online Safety (CSEA Content Reporting by Regulated User-to-User Service Providers) Regulations 2026
  (SI 2026/268), reg 8**: content reported to the NCA must be kept for one year from the date the report was sent,
  and the report's reference for five years.
- **EU, Terrorist Content Online Regulation (2021/784) art. 6**: removed terrorist content must be kept for six
  months.
- **US, 18 U.S.C. 2258A(h)**, if it applies: content reported to NCMEC must be kept for one year.
- **UK, Protection of Children Act 1978 s1B**: holding indecent images of children is an offence unless it is
  necessary for preventing, detecting or investigating crime. This is why the copy must stay private and
  access-controlled (the evidence bucket, with no public access, and the blocked quarantine) and why it is never
  opened.

`EvidenceRetention` (365 days from removal) covers these for now; retention counted from the date of the report to
the authorities is a follow-up.

## Giving evidence to the authorities

The evidence id is on the report (`content_report.evidence_id`). Download `{evidenceId}/record.json` and
`{evidenceId}/video.mp4` (or `{evidenceId}/source`) from the evidence bucket in the Cloudflare dashboard, or with any S3 client using the R2
credentials. Don't open the video. If the authorities ask for it to be kept longer, extend it:

```sql
UPDATE clip_evidence SET delete_after = delete_after + interval '1 year' WHERE id = '<evidence id>';
```

Report child sexual abuse material to the [IWF](https://report.iwf.org.uk/) (UK) and, for US users, to
[NCMEC's CyberTipline](https://report.cybertip.org/).

## Not yet covered

- The Discord Activity can still play a taken-down (not removed) clip.
- There's no report button on shared collections or in the Activity, and no way to undo a takedown from the admin
  page.
- An admin can't watch a clip under an open terrorism report, since its share link is hidden.
- Bunny's CDN isn't purged when a Bunny clip is removed; deleting the video at Bunny is relied on.
