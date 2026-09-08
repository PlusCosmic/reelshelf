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
  - an unknown identity creates a new account (no merging by email, since Discord's `identify` scope has none and Twitch's would need an extra consent);
  - when the challenge was started from `GET /auth/{provider}/link` by a signed-in user, the identity is attached to that account instead. The account id travels in the data-protected authentication properties and must match the current session when the provider returns.
- The oldest identity is the account's **primary identity**. The account's username, display name and avatar refresh from it at sign-in; signing in with a secondary identity refreshes only that identity's own profile. Unlinking the primary promotes the next-oldest and copies its profile onto the account.
- An account holds at most one identity per provider and can never drop its last one (`DELETE /api/me/identities/{provider}`). An identity already attached to another account cannot be linked; the settings page reports why.
- `whitelist.json` entries name a person by `DiscordId` and/or `TwitchId`. Any linked identity that matches applies the entry, and an entry with an explicit role wins over one without.
- Twitch's authorize call is sent with no scopes; reading the token owner's public profile through Helix needs none. `Twitch:Scopes` can widen this per deployment.

## Consequences

- Services take the account id, not a provider id. The per-request lookup by Discord id in every service is gone; the authenticated-user middleware is the single place that resolves the account.
- Usernames are no longer unique in practice: a Discord username and a Twitch login can coincide. Adding a collaborator by exact username refuses an ambiguous match and points at the suggestion list, which works by account id.
- Provider failures and cancelled consent screens redirect back to the app with `auth_error` (sign-in) or `link_error` (linking) instead of surfacing as a 500.
- Deployments need a Twitch application with `/auth/twitch/callback` registered and `TwitchClientId` / `TwitchClientSecret` set. Discord configuration is unchanged.
