# ADR-0009: Feature flags through PostHog, evaluated on the API

**Status:** Accepted (2026-10-26), amended the same day for PostHog's free plan (one project, prod only). Builds on ADR-0008 (roles and granted tiers in the database).

## Context

Reelshelf has no way to release a change gradually or to a chosen set of people. What it has:

- **Config switches** such as `Plans:Enforce`, `Migration:Enabled`, `LegendDetection:AutoDetect` and `Overflow:Enabled`. They apply to everyone at once, and changing one means editing infrastructure config and restarting the API.
- **Access**, which after ADR-0008 is two columns on the account: `app_user.role` (Viewer, Editor or Admin) and `app_user.granted_tier`, combined with subscriptions by `AccountTierService`. Admins change both at `/admin/accounts` without a deploy, and every change is logged. These decide what an account may do and what its plan includes, not how a change rolls out.

We want to turn a feature on for named accounts, for admins, for a tier, or for a growing percentage of everyone, and to switch it off again, without a deploy. We also want to learn PostHog, whose product analytics could later tell us whether a rollout changed anything.

The privacy policy (`frontend/src/routes/privacy.tsx`) says Reelshelf doesn't "use tracking or analytics scripts", and its cookies and local storage section lists only the sign-in cookie, the theme and the signed-in hint. Any change that breaks either statement has to update the policy in the same PR.

The signed-out front page is the PageSpeed target and must stay off the signed-in app's critical path.

### Options considered

- **A hosted flag service** (PostHog, LaunchDarkly, ConfigCat, GrowthBook Cloud, Flagsmith Cloud): targeting, percentage rollouts, a UI and audit history out of the box, at the price of a new third party. LaunchDarkly costs too much for this app.
- **A self-hosted open-source service** (Unleash, GrowthBook, Flagsmith, flagd): the same features with our data kept here, but another service, database and upgrade cycle to run.
- **`Microsoft.FeatureManagement` with flags kept in Postgres**: no third party, but we would build the admin page and the targeting rules ourselves and get no analytics.

## Decision

### PostHog, on EU Cloud, in two phases

PostHog provides the flags, and later product analytics.

- **EU Cloud** (`eu.posthog.com`, hosted in Frankfurt). Our servers and database are in Germany. Clips are moving to Cloudflare R2, whose bucket is in eastern North America, so most of what users store will be held in the US anyway, alongside the US providers the privacy policy already lists. EU Cloud is therefore a small gain, not a principle: what PostHog holds about accounts stays in the EU, with the account records it describes, and PostHog doesn't join the list of US processors. Phase 1 sends PostHog very little (below), so the difference is small now; it grows in phase 2, which would send usage events. Speed is no reason either way: flags are evaluated in-process and events are sent in the background. The choice is made once, because a PostHog project can't be moved between regions; changing later means a new project without its history.
- **One PostHog project, used by prod only.** PostHog's free plan has one project, and we don't pay for more. Dev has no PostHog key, so every flag there is off unless overridden in config (below). Sharing the project with dev was rejected: dev's database is a copy of prod's, so the same account ids exist in both, and a condition meant for dev would also switch the flag on in prod unless every condition also named an environment. Dev needs to run the code behind a flag, not to test PostHog's targeting, so config overrides are enough.
- **An account is known to PostHog only by its `app_user.id`.** Email, username and provider ids are never sent.

### Flags are for rollouts, not access

ADR-0008 settles that an account's access is its role and its tier, and that a narrower grant should be a new permission on a role, not something per account. Flags don't become a third way in:

- A flag decides whether an account sees a feature **that is being rolled out**. It never grants a permission or a tier, and it never stands in for one. A Pro-only feature checks the tier (`TierLimits`) as well as its flag, and an admin page checks its permission as well as its flag.
- A flag is temporary. Once a feature is out to everyone, the flag and its key are removed from the code. A feature that should stay limited to some accounts gets a permission or a tier limit instead.
- Turning a flag on for someone is a change in PostHog, not in Reelshelf, so it isn't logged in `account_access_change`. PostHog keeps its own history of flag changes.

### Phase 1: flags evaluated on the API, no browser script

- **Package.** `PostHog.AspNetCore`, which plugs PostHog into `Microsoft.FeatureManagement`, so code asks `IFeatureManager` or uses `[FeatureGate]`, and PostHog is only where the flags come from.
- **Who the request is for.** A context provider (a subclass of `PostHogFeatureFlagContextProvider`) reads `AuthenticatedUser` and gives the account id as the distinct id and these person properties:
  - `role`: `viewer`, `editor` or `admin`, from `app_user.role`;
  - `tier`: the account's current tier (`free`, `plus`, `pro` or `unlimited`) from `AccountTierService.GetTier(user)`;
  - `granted_tier`: `plus`, `pro`, `unlimited` or `none`, from `app_user.granted_tier`;
  - `signed_up_at`.

  `role` and `granted_tier` come from the user row that `AuthenticatedUserMiddleware` already reads, so they cost nothing. `tier` needs the subscription lookup, so the provider asks for it once per request, and only when a flag is checked.

- **What the properties allow.** Every rollout is set up in PostHog's UI:
  - named accounts, by account id (shown at `/admin/accounts`);
  - admins: `role = admin`;
  - a tier: `tier in (pro, unlimited)`;
  - testers: accounts granted a tier through ADR-0008 are usually friends and testers, so `granted_tier != none` is a ready-made early-access group;
  - a percentage of everyone;
  - off, as a kill switch.
- **When changes apply.** The properties are worked out from the database on every check rather than stored at PostHog, so a role or granted tier changed at `/admin/accounts` changes which flags an account sees on its next request, as ADR-0008's own changes do. A change to a flag in PostHog applies once the API next downloads the definitions, within the poll interval (below), so switching a flag off takes up to 5 minutes. PostHog cohorts built from stored person properties are not used, because phase 1 stores none.
- **Local evaluation.** The SDK downloads the flag definitions and evaluates them in-process, so a flag check makes no network call. Every property a flag's conditions use must be passed, or the SDK can't decide locally. The SDK's `$feature_flag_called` events are turned off, so phase 1 sends PostHog nothing about individual accounts. The only account data PostHog holds is the ids an admin types into a flag's conditions.
- **Polling every 5 minutes, to stay on the free plan.** PostHog bills each download of the definitions as 10 flag requests, and the free plan includes 1 million flag requests a month. The SDK's default 30-second poll would use about 864,000 a month for one always-running instance, so the API polls every 5 minutes (`PostHog:PollInterval`), about 86,400 a month. Only prod polls, because dev has no key. A second always-running instance with a key, or a shorter interval, needs checking against the allowance first.
- **Config overrides.** `FeatureFlags:Overrides:<key>` (`true` or `false`) fixes a flag's value on that instance without asking PostHog. Dev, local runs and tests use it to turn on the code they are working on. Prod sets none, and an override set there would win over PostHog, so it is only for an emergency.
- **Without a request.** Background services (summaries, the over-limit sweep, the transcode claim) call a small `FeatureFlags` service that builds the same properties for an account id, reading the role and granted tier from `app_user` and the tier from `AccountTierService.GetTier(userId)`, and asks the PostHog client directly.
- **Config.** `PostHog:ProjectApiKey`, `PostHog:PersonalApiKey` (or whichever key PostHog requires for local evaluation when this is built, since it has said personal API keys for this will be deprecated) and `PostHog:HostUrl`, set on prod only. Without a key every flag reads as off unless overridden, and nothing is sent, as with Resend and Billing. The `OpenApi` environment needs no key.
- **The frontend.** `GET /api/me/features` (with `RequireAuthorization()`) answers the keys of the flags that are on for the caller, and a `useFeature("key")` hook reads it through React Query. The API is the only place flags are evaluated, so the browser can't switch one on. The generated client is regenerated with `bun run generate:api-client`.
- **Flag keys** live as constants in one class (`FeatureFlagKeys`), so a removed flag is a compile error rather than a silent `false`.
- **Tests** replace `IFeatureManager` / `FeatureFlags` with a fake and never call PostHog.
- **Privacy policy.** PostHog is named as a processor that receives account ids to decide which features an account sees, with its data held in the EU. Neither the "no analytics scripts" sentence nor the cookies section changes.

### Phase 2 (later, optional): product analytics

1. **Events from the API** through `IPostHogClient.Capture`, for what is worth measuring: `clip_uploaded`, `twitch_import_completed`, `checkout_started`, `subscription_started`, `plan_downgraded`. There is no browser script and no cookie, and ad blockers can't drop them. This is what lets a flagged rollout be measured. Events carry `tier` and `granted_tier`, so testers' activity can be told apart from paying customers'.
2. **Browser analytics with `posthog-js`**, only if funnels, pageviews or session replay are wanted:
   - loaded lazily inside the signed-in `AppShell` chunk, never on the landing page or in the Discord Activity;
   - sent through our own domain (`/ingest`, proxied to PostHog EU) so ad blockers don't drop it;
   - `person_profiles: 'identified_only'`, with cookieless mode or a consent banner, since UK and EU rules require consent for analytics cookies;
   - session replay off, or on with every input and text masked.

   This is the step at which the policy's "no analytics scripts" sentence and its cookies section change.

Phase 2 gets its own ADR when it is taken up.

### Account deletion

`AccountDeletionBackgroundService` deletes the account's person in PostHog through PostHog's API, alongside its videos and Stripe subscriptions, once anything about the account is stored there. In phase 1 that is only an account id typed into a flag's conditions, which an admin removes by hand; deletion through the API is needed from phase 2.

## Consequences

- A flag can be switched for named accounts, a role, a tier, granted testers or a percentage without a deploy, and switched off as a kill switch within 5 minutes.
- Dev can't try a flag's targeting rules before prod; it runs flagged code through config overrides. A rollout's first audience on prod is admins or granted testers instead.
- The free plan's 1 million flag requests a month bound how many instances can poll and how often.
- Access stays where ADR-0008 put it. Flags only hide or show features that are being rolled out, so removing a flag never removes anyone's access.
- Flag checks stay in-process and don't slow requests, but they depend on the definitions the SDK last fetched; if PostHog is down, the last fetched definitions still apply, and with none fetched every flag is off.
- PostHog becomes a third party, in the EU, holding the account ids used in flag conditions. Phase 2 would widen that to usage events.
- Config switches that are really operations settings (`Migration:Enabled`, `Overflow:Enabled`, `Plans:Enforce`) stay in config.
