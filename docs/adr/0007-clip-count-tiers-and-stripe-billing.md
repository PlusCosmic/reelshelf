# ADR-0007: Clip-count tiers and paid plans through Stripe

**Status:** Proposed (2026-10-23). Supersedes the storage limit in ADR-0002 once implemented; ADR-0002's other decisions (open sign-up, per-endpoint authorization, reserve-then-attach) stand.

## Context

ADR-0002 opened sign-up with two **Storage Tiers**: 25 GiB for everyone (`Storage:DefaultLimitBytes`) and unlimited for accounts listed in `whitelist.json`. Nobody is on the 25 GiB tier yet: everyone using Reelshelf today is whitelisted. That leaves room to define tiers from scratch before strangers arrive.

Two things have changed since ADR-0002:

- **Clips are ours to host.** New uploads go to R2 (`VideoHost:NewUploads=r2`) and the Bunny migration is moving the rest. What a clip costs is now known and small (below), so limits can be set by what makes people upgrade rather than by fear of an unbounded bill.
- **People should be able to pay.** Reelshelf will offer two paid plans alongside a free one, and the free one will eventually carry minimal ads.

### What a clip costs

The worker targets 4,000 kbps video at 30 fps or below and 6,000 kbps above 32 fps, plus 128 kbps Opus, fitted in 1080p (`workers/Reelshelf.Worker`). VBR can overshoot a little on high motion.

| Clip          | Approximate output |
| ------------- | ------------------ |
| 60 s, 60 fps  | 46 MB              |
| 120 s, 30 fps | 62 MB              |
| 120 s, 60 fps | 92 MB              |

- **Storage.** R2 Standard is $0.015/GB-month, Infrequent Access (IA) $0.01/GB-month after the bucket's 7-day transition. A worst-case 120 s clip in IA costs about $0.0009 a month, $0.011 a year. IA bills a 30-day minimum and $0.01/GB to read back, which only matters for clips read cold (missed by Cloudflare's cache, which holds `immutable` outputs) more than about once every two months.
- **Encoding.** Free on the self-hosted worker, about $0.004 a clip on the Batch overflow pool. The pool's uploads to R2 are GCP internet egress on a Standard-tier IP, inside the 200 GB a month GCP gives free (roughly 2,000 worst-case or 4,000 to 6,000 typical overflow encodes).
- **Delivery.** R2 egress is free.
- **AI features cost more than storage.** Legend detection (a vision model call per Apex clip, plus escalations), transcription, summaries and embeddings each cost more per clip than a year of keeping it.

So a tier's storage is bounded by clips × maximum size × $0.01/GB-month, and every tier below stays far inside its price even at its cap.

## Decision

### Four tiers, limited by clip count and clip length

|                                       | Free                | Plus                 | Pro                  | Unlimited        |
| ------------------------------------- | ------------------- | -------------------- | -------------------- | ---------------- |
| Who                                   | Everyone by default | £3/month or £30/year | £8/month or £80/year | `whitelist.json` |
| Clips kept                            | 100                 | 1,000                | 5,000                | No limit         |
| Longest clip                          | 60 s                | 120 s                | 120 s                | No limit         |
| New clips per 30 days                 | 100                 | 1,000                | 5,000                | No limit         |
| Apex legend detection                 | Yes                 | Yes                  | Yes                  | Yes              |
| Ads (later)                           | Yes                 | No                   | No                   | No               |
| Transcript search and summaries       | No                  | No                   | Yes                  | Yes              |
| Worst-case monthly storage at the cap | ~$0.05              | ~$0.92               | ~$4.60               | —                |

- **The tier counts clips, not bytes.** A clip count with a length cap bounds cost just as well and is easier to explain than gigabytes. The byte quota stays as a hidden backstop (`Storage:BackstopBytes`, well above any tier's worst case) so a pathological file or a bug can't run up the bill.
- **Clip Count is every clip the account owns, in any state.** A reserved upload counts from the moment its row is created, as storage does today, so in-flight uploads can't overshoot the cap. Deleting a clip frees its slot.
- **New clips per 30 days is counted separately.** Each upload costs an encode, so deleting and re-uploading must not be free for ever. It counts clip creations in a rolling 30 days, recorded in a small per-account log that survives deleting the clip, and sits at the tier's clip limit so no ordinary user meets it.
- **The length cap is enforced when the upload completes.** R2's completion already runs ffprobe (`FfprobeUploadProbe`); a duration over the owner's cap marks the clip `upload_failed`, deletes the object and answers 422 with the reason, like any other probe failure. The uploader reads the duration locally first so it can refuse before uploading, and offers to trim. Twitch imports are capped at 60 s by Twitch but go through the same check. Clips that already exist are never re-checked against the length cap, so a lower cap never removes anything.
- **The "admin/whitelisted" tier is called Unlimited**, so it doesn't collide with the `Admin` **Role**. It still comes from `whitelist.json`; the file keeps its role overrides. Roles and tiers stay independent.
- **Legend detection runs on every tier**, as it does now, escalation included. It is the most distinctive feature for Apex players, and its cost is watched on the evals page rather than gated.
- **The prices are placeholders** and can change in Stripe without a deploy (below). Each price carries currency options for USD, EUR and CAD besides GBP (placeholders: Plus $4/$40, €3.50/€35, C$5/C$50; Pro $10/$100, €9/€90, C$13/C$130), and Checkout shows the one for the customer's location, sparing overseas customers conversion fees.

### Going over the limit: 14 days, then an export and deletion

An account goes over its tier's limit when its tier drops: a subscription ends, a downgrade takes effect, or a `whitelist.json` entry is removed. Clips over the limit are kept for 14 days, then exported and deleted.

- **Fourteen days, not longer.** A grace period as long as a billing period would let someone keep a paid library by paying one month in two. Fourteen days is enough to download what matters; resubscribing within it cancels everything below.
- **The grace starts when the tier drops**, recorded as `app_user.over_limit_since` by an hourly sweep (`TierEnforcementService`, never in `OpenApi`), which also clears it once the account is back under its limit by any route. `past_due` still counts as paid, so a failing card doesn't start it while Stripe is retrying. A cancellation scheduled for the period's end warns as soon as it is scheduled, so the download can happen before the paid period ends.
- **The newest clips are kept by default.** The library marks which clips are over the limit, and the owner can swap any of them for a kept one during the grace. Clips over 60 s that end up on Free are kept if they fall within the limit; the length cap only applies to new uploads.
- **Downloading them is one button.** "Download clips over the limit" starts an export of exactly those clips (below), whenever the owner likes during the grace; single clips download as they do today.
- **After 14 days the clips over the limit are exported, then deleted.** The sweep queues the export; only once it has succeeded are the clips deleted through `ClipVideoDeletion`, as the owner deleting them would. A failed export is retried and nothing is deleted meanwhile.
- **Emails** go out when the grace starts, three days before it ends, and when the export is ready, with a link to it.

### Exports

- **An export is a new `export` job on the transcode queue**, so a worker builds it and the API never streams gigabytes. Its input lists the clips; the claim hands out a presigned GET for each clip's current `av1.mp4` and presigned PUTs for the parts. The worker writes stored (uncompressed, Zip64) zips, since AV1 doesn't compress, split into parts of at most 4 GB so each part is one PUT under R2's 5 GB single-upload limit. Files are named from the clip's title and date, made unique within the export. Exports have the lowest priority, below `migrate`.
- **Parts live under `exports/{userId}/{exportId}/` for 28 days**, deleted by a bucket lifecycle rule on that prefix, which also never moves them to Infrequent Access (its read fee and 30-day minimum would cost more than it saves). An `export` row records the clips, the parts and the expiry.
- **The email links to a Reelshelf page, not to R2.** Presigned URLs can't outlive 7 days, so `/exports/{exportId}` is owner-only and hands out a 10-minute presigned GET per part on click, the way clip downloads work.
- **Worst case** is a lapsed Pro account's 4,900 clips over the Free limit, about 450 GB held for 28 days in R2 Standard: roughly $6, once.

### Billing through Stripe Managed Payments

- **Stripe is the merchant of record**, through Managed Payments, at about 3.5% on top of Stripe's processing and Billing fees. Reelshelf is a UK sole trader under the VAT threshold, but there is no threshold for selling digital services to EU consumers (or in India, South Korea and others); a merchant of record handles every country's tax, fraud and disputes, so paid plans can be sold everywhere from the first day. Stripe Tax with a country allowlist was the cheaper alternative and was rejected for the code and attention it costs.
- **Customers see Link.** Checkout, receipts and transaction support are Link's ("Sold through Link"), and statements read `LINK.COM* REELSHELF`. Our confirmation email and terms say so, since an unfamiliar statement line is a common cause of chargebacks.
- **One Stripe product per paid tier, two prices each.** `Reelshelf Plus` and `Reelshelf Pro`, both with tax code `txcd_10103000` (SaaS, personal use), each with a monthly and a yearly price. The API finds prices by lookup key (`plus_monthly`, `plus_yearly`, `pro_monthly`, `pro_yearly`) and maps a product to a tier through `Billing:Tiers`, so changing a price is a Stripe change only. Free and Unlimited have no product.
- **Subscriptions are only created through Checkout**, which Managed Payments requires. `POST /api/billing/checkout` takes a lookup key and creates a Checkout Session with `managed_payments[enabled]=true`, `mode=subscription`, `client_reference_id` the account id and the account's existing Stripe customer when it has one. An account with a live subscription is sent to the Customer Portal instead, so nobody ends up with two.
- **Changes go through the Customer Portal.** `POST /api/billing/portal` opens Stripe's portal for upgrading, downgrading, switching between monthly and yearly, cancelling and updating the card. Managed Payments doesn't let the API change a subscription's default payment method, so the portal is the only place for that anyway.
- **Webhooks are the only source of truth.** `POST /api/billing/webhook` is `AllowAnonymous()` and verified by Stripe's signature (`Billing:WebhookSecret`), as the Bunny webhook checks its secret. It handles `checkout.session.completed`, `customer.subscription.created`, `.updated` and `.deleted`, and `invoice.payment_failed`. Each event re-reads the subscription from Stripe and upserts it rather than trusting the event's copy, since events can arrive out of order, and processed event ids are recorded so a redelivery does nothing. The success page never grants anything by itself.
- **Data.** `app_user.stripe_customer_id`, and a `subscription` row per account (Stripe subscription id, status, price lookup key, tier, current period end, cancel at period end). A `stripe_event` table holds processed event ids.
- **The tier is derived, never stored.** An account's tier is Unlimited when `whitelist.json` lists one of its identities, else its subscription's tier while the status is `active`, `trialing` or `past_due` (Stripe is still retrying the card), else Free. Nothing has to be kept in step.
- **Environments.** Dev uses the Stripe sandbox, with its own keys and webhook endpoint, and the Stripe CLI to forward webhooks into `clips-dev`. Prod uses the live account. `Billing:SecretKey`, `Billing:WebhookSecret` and `Billing:Tiers` follow the API's other settings; with no secret key the billing endpoints answer 404 and everyone is Free or Unlimited, so a deployment without Stripe still works.

### What the tier changes elsewhere

- **Enforcement.** `ClipService.CreateClip` checks the clip count and the 30-day count under the existing per-owner advisory lock, where it checks storage today. Refusals stay 403 with a reason the UI can show.
- **Gated features.** Transcription and summaries (`ClipTranscription:AutoQueue`, `ClipSummary:AutoQueue`), which now check `whitelist.json`, check for Pro or Unlimited instead.
- **`GET /api/me/plan`** replaces `GET /api/me/storage`: tier, clip count and limit, 30-day count and limit, longest clip, and the subscription's state and renewal or end date. The generated client changes with it.
- **The nearly-full email** (`StorageWarningService`) warns at `Storage:WarnAtPercent` (default 90) of the clip limit instead of the byte limit.

## Consequences

- Nobody loses anything: every current user is whitelisted, so they are Unlimited. The 25 GiB default disappears without anyone having been on it.
- Users can no longer judge their usage in bytes; the UI shows clips used and the longest clip allowed instead.
- Clips over a lapsed account's limit are deleted after 14 days, so nobody keeps a paid library without paying for it, and the export means nobody loses their clips without a copy. Someone who resubscribes after the deletion starts from what they kept; the export is the way back.
- An account over its limit can't add clips until it's under, from the moment its tier drops.
- Deleted clips leave the playlists and share links they were in, exactly as when the owner deletes them.
- The free tier's worst case is a few cents a month, so the ads only need to cover that. They are a separate decision: an ad network is a new third party, needs a consent banner and a privacy policy change, and would be left out of the Discord Activity.
- Stripe processing, Billing's 0.7% and Managed Payments' 3.5% take about 12% of a £3 monthly charge but about 6% of a £30 yearly one, so the UI leads with yearly prices.
- Stripe sends receipts, renewal reminders and failed-payment emails from Link; Reelshelf sends none about billing.
- The privacy policy names Stripe (as Link) as the seller handling payment data under its own policy, and the policy states the 14-day grace and the 28-day export retention. The terms cover subscriptions, renewal, cancellation, what happens to clips over the limit, and the UK's 14-day cancellation right for digital services, which a subscriber gives up by agreeing to start at once. Both change in the PR that adds billing.
- CONTEXT.md replaces **Storage Tier** and **Storage Usage** with **Tier** and **Clip Count** when this is implemented.
