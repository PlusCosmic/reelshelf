# Reports, takedowns and removals

Anyone watching a share link can report the clip. Admins deal with reports at `/admin/reports`. The code is in
`api/Moderation/`; the tables are `content_report` (V51) and `clip_evidence` (V52).

## What each decision does

| Report reason                          | Decision  | What happens                                                                                                                                                                                                                                      |
| -------------------------------------- | --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Any                                    | Dismiss   | The report is closed. The clip is left alone.                                                                                                                                                                                                     |
| Anything but child safety or terrorism | Take down | Sharing is blocked (`clip.sharing_blocked_at`), the clip's share links are revoked, and every open report on it is closed. The owner is emailed that the clip broke the terms, without the rule or the reporter. The clip stays in their library. |
| Child safety or terrorism              | Remove    | See below. The owner isn't emailed.                                                                                                                                                                                                               |

A child-safety or terrorism report hides the share link the moment it is filed, and every Admin with an email
address is mailed. While it is open the clip is held as possible evidence: the share link stays hidden, the owner
can't delete the clip (the over-limit sweep skips it too), and the owner can't delete their account. Both refusals
say only that it can't be done right now.

### Removing a clip

One transaction (`ContentReportStatements.Remove`):

1. snapshots the clip row (with its game's name) and its owner (username, email and every linked sign-in) into a new
   `clip_evidence` record, kept for `Moderation:EvidenceRetention` (365 days);
2. suspends the owner (`app_user.suspended_at`, recorded in `account_access_change`), unless they are an Admin;
3. closes every open report on the clip as `removed`;
4. deletes the clip row, so nothing in Reelshelf lists or plays it from then on.

`ClipEvidenceProcessor` (run by `ClipEvidenceBackgroundService`) then, retrying from wherever it stopped:

1. writes `{evidenceId}/record.json` (the snapshots and the decision) and copies the video to
   `{evidenceId}/video.mp4` in the evidence bucket: an R2 clip's `av1.mp4`, or a Bunny clip's HLS stream remuxed to
   MP4, exactly as an export reads them;
2. deletes the video from its host, every R2 version of the clip, and any Bunny copy;
3. purges every output URL of versions 1 to `video_version + 1` from the media domain's cache (outputs are cached
   `immutable` for a year, so deleting them from R2 isn't enough);
4. after `Moderation:SweepAfter` (3 hours), deletes and purges the clip's R2 folder once more, for anything a
   transcode worker uploaded with presigned URLs it held at removal.

After the retention, the evidence files and record are deleted.

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

Other settings: `Moderation:ContactEmail` (where owners dispute a takedown), `Moderation:ReportsPerMinute` (5),
`EvidenceMaxAttempts` (5), `EvidenceRetryAfter` (5 minutes), `EvidenceStaleAfter` (30 minutes) and `SweepAfter`
(3 hours, which must stay above `Transcoding:PresignLifetime`).

## When an evidence copy fails

After `EvidenceMaxAttempts` the record is `failed` and the admin page shows the last error. Fix the cause (usually
the bucket or token), then retry it:

```sql
UPDATE clip_evidence SET status = 'pending', attempts = 0, available_at = now(), last_error = NULL
WHERE id = '<evidence id>';
```

It resumes from the step it stopped at: nothing already copied is copied again, and nothing is deleted before the
copy exists.

## Giving evidence to the authorities

The evidence id is on the report (`content_report.evidence_id`). Download `{evidenceId}/record.json` and
`{evidenceId}/video.mp4` from the evidence bucket in the Cloudflare dashboard, or with any S3 client using the R2
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
