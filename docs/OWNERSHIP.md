# Ownership Notes

This repository now contains one product: Clips.

## Clips

Paths:

- `api/`
- `frontend/`
- `migrations/`
- `Dockerfile`
- `Nucleus.sln`

Clips owns upload, library, playlists, Bunny integration, game/category metadata, auth, database migrations, and deployment assets.

## Review Checklist

- Does this change alter auth, roles, Discord users, permissions, or game categories?
- Does this change require a database migration?
- Does this change update generated API client files without updating or documenting the OpenAPI source/regeneration step?
- Does this change affect deployment order, shared volumes, data-protection keys, or network assumptions?
