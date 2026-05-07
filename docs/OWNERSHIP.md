# Ownership notes

This monorepo contains several product domains that share a few core building blocks. Ownership is intentionally lightweight for now: `.github/CODEOWNERS` routes every area to the repository owner while this document records the boundaries future teams or maintainers should preserve.

## Current review routing

All paths currently resolve to `@PlusCosmic` in `.github/CODEOWNERS`. The file is split by domain anyway so ownership can be narrowed later without rediscovering the repository shape.

## Product domains

### Personal site

Path: `apps/personal-site/`

The personal site is its own frontend app. Changes here should avoid coupling to Clips or Minecraft runtime concerns unless they are explicitly shared through packages.

### Clips

Paths:

- `services/clips/api/`
- `services/clips/frontend/`
- `services/clips/Dockerfile`

Clips owns clip upload, library, playlists, Bunny integration, game/category presentation, and its service-specific deployment assets.

### Minecraft

Paths:

- `services/minecraft/api/`
- `services/minecraft/frontend/`
- `services/minecraft/Dockerfile`

Minecraft owns server provisioning, container lifecycle controls, console/RCON flows, file management, backups, and its service-specific deployment assets.

## Shared frontend ownership

Paths:

- `packages/shared/`
- `packages/ui/`
- `packages/eslint-config/`
- `packages/typescript-config/`
- `packages/clips-api-client/`
- `packages/minecraft-api-client/`
- `openapitools.json`

These packages are shared by multiple apps. Changes should be reviewed as cross-app changes even when they are motivated by one app. Generated API client changes should be paired with the OpenAPI source or documented regeneration command that produced them. See `docs/API_CLIENT_PIPELINE.md` for generation commands and drift checks.

## Shared backend ownership

Paths:

- `packages/nucleus-shared/`
- `tools/Nucleus.Migrations/`
- `Nucleus.sln`

The backend split is a shared Nucleus PostgreSQL database plus feature APIs, not independently owned service databases.

`packages/nucleus-shared` owns shared backend mechanics such as auth, Discord identity, permissions, game categories, exceptions, and middleware used by both feature APIs.

`tools/Nucleus.Migrations` is the single owner of schema changes for the shared Nucleus database. Do not add competing API-startup migration paths for Clips or Minecraft. Deployment should run one explicit migration step before starting code that depends on the schema.

## Practical review checklist

Before approving cross-domain work, check:

- Does this change alter shared auth, roles, Discord users, permissions, or game categories?
- Does this change require a shared database migration?
- Does this change update generated API client files without updating or documenting the OpenAPI source/regeneration step?
- Does this change create drift between Clips and Minecraft startup, auth, CORS, OpenAPI, or static hosting patterns?
- Does this change affect deployment order, shared volumes, data-protection keys, or network assumptions?

If the answer is yes, treat the PR as shared Nucleus ownership work rather than a single feature-app change.
