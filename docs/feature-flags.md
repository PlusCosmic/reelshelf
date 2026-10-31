# Feature flags

How to add, roll out and remove a feature flag. The decision and its reasons are in [ADR-0009](adr/0009-posthog-feature-flags.md).

## Setting up PostHog (once)

1. Create one project on PostHog **EU Cloud** (`eu.posthog.com`). The free plan has one project, and it is prod's.
2. Copy the project token, and create a secret key: a Personal API Key with read access to feature flags, or a Project Secret API Key.
3. Add both to prod's secrets as `PostHog__ProjectToken` and `PostHog__SecretKey`. Nothing else is needed: the host defaults to EU Cloud and the poll to 5 minutes.
4. Dev gets no PostHog settings. Its flags are off unless overridden (below).

A project token without the secret key fails startup, because the SDK would otherwise ask PostHog on every check.

## Flags in use

| Key          | What it rolls out                                                              |
| ------------ | ------------------------------------------------------------------------------ |
| `paid-plans` | Buying and changing a paid plan ([billing](billing.md#rolling-out-paid-plans)) |

## Adding a flag

1. Pick a key in kebab case, such as `new-uploader`.
2. Add it to `api/FeatureFlags/FeatureFlagKeys.cs`, as a constant and in `All`.
3. Add it to `featureFlagKeys` in `frontend/src/shared/services/featureFlags.ts`.
4. Check it:
   - in an endpoint: `.RequireFeature(FeatureFlagKeys.NewUploader)` after `RequireAuthorization()`, which answers 404 while it is off;
   - in other API code: `FeatureFlagService.IsEnabledAsync(key, user)`, or `IsEnabledAsync(key, accountId)` outside a request;
   - in the frontend: `useFeature("new-uploader")`, which is false until the flags have loaded.

   A flag never stands in for access: a Pro-only feature also checks the tier, and an admin page its permission.

5. Create the flag in PostHog with the same key. Until it exists there, it is off for everyone in prod.

## Trying it on dev or locally

Set `FeatureFlags__Overrides__new-uploader=true` (or `FeatureFlags:Overrides:new-uploader` in `appsettings.Development.json` or user secrets). An override wins over PostHog on that instance for every account. Prod sets none.

## Rolling it out in PostHog

Each release condition is a set of properties plus a rollout percentage. The API evaluates them locally against these person properties, worked out from the database on every check and never sent to PostHog:

| Property       | Values                             |
| -------------- | ---------------------------------- |
| `role`         | `viewer`, `editor`, `admin`        |
| `tier`         | `free`, `plus`, `pro`, `unlimited` |
| `granted_tier` | `none`, `plus`, `pro`, `unlimited` |

Target a named account by its distinct id, the account id shown under the account at `/admin/accounts`. A typical rollout:

1. `role = admin`, 100%.
2. Add `granted_tier != none`, 100% (friends and testers).
3. Everyone, 10%, then 50%, then 100%. Percentages are by account id, so an account that is in stays in as the share grows.

Don't build conditions on PostHog cohorts or stored person properties: nothing is stored at PostHog, so they match nobody.

A change in PostHog reaches the API within 5 minutes (the poll interval), and the frontend within a minute after that.

## Switching it off

Turn the flag off in PostHog (or set its rollout to 0%). Every account loses it within about 6 minutes, with no deploy. In an emergency where PostHog itself is the problem, `FeatureFlags__Overrides__<key>=false` on prod and a restart switches it off at once.

## Removing a flag

Once the feature is out to everyone:

1. Delete its constant from `FeatureFlagKeys` and its key from `featureFlagKeys`; the build then fails at every check.
2. Remove each check, keeping the "on" path.
3. Delete the flag in PostHog after the deploy.

## Account deletion

Nothing about an account is sent to PostHog. If an admin typed an account's id into a flag's conditions, remove it there when the account is deleted.

## Staying on the free plan

Each download of the definitions counts as 10 flag requests against the free plan's million a month. One always-running instance polling every 5 minutes uses about 86,400. Check the allowance before giving another always-running instance a PostHog key, or shortening `PostHog:FeatureFlagPollInterval`.
