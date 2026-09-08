# ADR-0003: Linked identities across sign-in providers

**Status:** Accepted (2026-09-08)

## Context

Reelshelf only knew Discord: the user table was `discord_user`, its natural key was the Discord id, the session cookie carried that id, and every service resolved the caller by it. Adding Twitch as a second way to sign in cannot mean a second, disconnected library for someone who uses both, so accounts need to exist independently of any one provider.

## Decision

- Accounts become provider-neutral. `discord_user` is renamed `app_user` and loses `discord_id`; the avatar column stores a full URL. Foreign keys are unchanged.
- Each provider login is a `user_identity` row (`provider`, `provider_user_id`, unique together) pointing at one account. Existing accounts get their Discord login migrated in as their first identity.
- The session cookie's subject is the account id. Sessions issued before this change carried a Discord id and are treated as signed out, so users sign in once more after deployment.
- OAuth handlers (Discord, Twitch) sign in to a short-lived external cookie. `GET /auth/post-login-redirect` reads it, resolves the account, and issues the real session:
  - a known identity signs in to its account;
  - an unknown identity creates a new account. Email is never used to match a sign-in to an existing account: whoever controls an address at one provider must not be able to attach themselves to an account at the other, and Discord does not guarantee the address is current;
  - when the challenge was started from `GET /auth/{provider}/link` by a signed-in user, the identity is attached to that account instead. The account id travels in the data-protected authentication properties and must match the current session when the provider returns.
- The oldest identity is the account's **primary identity**. The account's username, display name and avatar refresh from it at sign-in; signing in with a secondary identity refreshes only that identity's own profile. Unlinking the primary promotes the next-oldest and copies its profile onto the account.
- An account holds at most one identity per provider (enforced by a `(user_id, provider)` unique constraint as well as the service) and can never drop its last one (`DELETE /api/me/identities/{provider}`). Link and unlink run under a per-account advisory lock inside one transaction, so concurrent requests cannot strip the last identity or double-link a provider, and a failed unlink rolls back rather than leaving the account showing a removed identity's profile. An identity already attached to another account cannot be linked; the settings page reports why.
- `whitelist.json` entries name a person by `DiscordId` and/or `TwitchId`. Any linked identity that matches applies the entry, and an entry with an explicit role wins over one without.
- Both providers are asked for the email address (`identify email` on Discord, `user:read:email` on Twitch) and it is stored per identity; Discord addresses are kept only when Discord reports them verified. The **account email** is separate and user-owned: on the first sign-in after this change (new and existing accounts alike) the app asks where account mail should go, prefilled with a provider address, and the user can change it, skip, or edit it later in Settings (`PUT /api/me/email`). `onboarding_completed_at` records that they were asked. Account mail goes out through Resend (`Resend:ApiKey`, `Email:From`); without a key the app logs instead of sending. Two notices exist: one when a new sign-in method is linked, so a hijacked link can be undone from Settings, and one when clip usage first crosses `Storage:WarnAtPercent` (default 90) of the limit. The storage notice fires once per crossing: `storage_warned_at` is set when it is sent and cleared once usage drops back under the threshold. Sending is best effort and never fails the request that triggered it.

## Consequences

- Services take the account id, not a provider id. The per-request lookup by Discord id in every service is gone; the authenticated-user middleware is the single place that resolves the account.
- Usernames are no longer unique in practice: a Discord username and a Twitch login can coincide. Adding a collaborator by exact username refuses an ambiguous match and points at the suggestion list, which works by account id.
- Provider failures and cancelled consent screens redirect back to the app with `auth_error` (sign-in) or `link_error` (linking) instead of surfacing as a 500.
- Deployments need a Twitch application with `/auth/twitch/callback` registered and `TwitchClientId` / `TwitchClientSecret` set. Discord configuration is unchanged.
