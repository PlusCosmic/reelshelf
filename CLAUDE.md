# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Plus Cosmic Development is a Turborepo monorepo containing video clip management applications and shared packages. The project uses Bun workspaces with Vite for fast development builds.

**Backend**: .NET APIs in `services/*/api` using Dapper/Npgsql
**Main Branch**: `develop` (use this for PRs, not `main`)

## Applications

### `services/clips/frontend`
Video/media clip management application using TanStack Router for file-based routing. Routes are auto-generated from `src/routes/` directory structure via the TanStack Router plugin.

**Key Features**:
- File-based routing with `__root.tsx` providing AppShell layout
- Video uploads using tus-js-client for resumable uploads
- Uses Mantine UI components with modals and notifications
- TanStack Router DevTools and React DevTools integrated in footer

**Route Structure**: Routes in `src/routes/` auto-generate `routeTree.gen.ts`. The `__root.tsx` provides the AppShell layout wrapper for all routes.

### `services/minecraft/frontend`
Minecraft server management application using TanStack Router and Mantine.

**Key Features**:
- Live console and server status using React Query and WebSockets
- File editor for server configuration files
- Container lifecycle controls backed by the Minecraft API

### `apps/personal-site`
Personal/portfolio site built with React, Vite, and Mantine.

## Packages

### @repo/nucleus-api-client
**Auto-generated API client** from OpenAPI specification. Never manually edit the `src/` directory.

**Regeneration Process** (from `packages/nucleus-api-client/`):
1. Update `Nucleus.json` from the current API OpenAPI output.
2. Run: `bun run generate`
3. Run: `bun run build`

The `Nucleus.json` OpenAPI spec must be updated before regenerating the client. Generated Markdown docs are intentionally excluded.

### @repo/shared
Shared business logic, services, and hooks used by both apps.

**Path Exports**:
- `@repo/shared/api-config` - API configuration (baseUrl, bunnyBaseUrl)
- `@repo/shared/services/auth` - Authentication service
- `@repo/shared/services/user` - User data fetching
- `@repo/shared/services/categories` - Categories management
- `@repo/shared/hooks/useDebouncedValue` - Debouncing hook

**Core Services**:
- `services/http.ts` - HTTP utilities (`getJson`, `tryGetJson`, `withQuery`, `HttpError`)
- `services/clips.ts` - Video clip operations (fetch, create, tag, delete, view tracking)
- `services/minecraft.ts` - Minecraft server, console, file, backup, and container operations
- `services/ffmpeg.ts` - Video download functionality

**API Configuration**: Uses `VITE_API_BASE_URL` environment variable, defaults to `/api` for same-origin deployments.

### @repo/ui
Shared UI components using Mantine. Components located in `components/` directory (no `src/`).

**Components**:
- `Footer` - Standard footer component
- `UserAvatar` - User avatar with optional login display
- `LoginButton` - OAuth login button

## Development Commands

```sh
# Development (all apps)
bun run dev

# Build all apps and packages
bun run build

# Lint all packages
bun run lint

# Format code
bun run format

# Apply database migrations explicitly
bun run migrate

# Individual frontend development
cd services/clips/frontend
bun run dev

cd services/minecraft/frontend
bun run dev

cd apps/personal-site
bun run dev

# Install a package in a specific workspace
bun add <package> --cwd services/<service-name>/frontend
```

## Database Migrations

Database migrations live in `tools/Nucleus.Migrations/db/migrations` and are applied with the explicit Evolve runner. Do not add migration execution to API startup.

The consolidated baseline is `V16` because the legacy combined backend used Evolve versions `V1` through `V15`. For first deployment to an existing production database with no Evolve changelog, use `bun run migrate:adopt-existing` once so the baseline is recorded without executing its SQL.

## Testing

### First-party tests
```sh
bun run test
```

### Testing Visual/Frontend Changes
When making visual or frontend changes in TypeScript applications, verify functionality in a browser before completing the task.

## Architecture Notes

### Routing Differences
- **clips frontend**: TanStack Router with file-based routing
- **minecraft frontend**: TanStack Router with file-based routing
- **personal site**: React with route-like page components

### State Management
- React Query for server state
- Jotai where the clips UI shares filter state

### API Client Usage
Apps can use either:
1. Auto-generated `@repo/nucleus-api-client` (TypeScript Fetch API)
2. Services from `@repo/shared` (wrapper functions using custom HTTP utilities)

The shared services provide a simpler interface and are preferred for common operations.

### Build Pipeline
Turborepo orchestrates builds with:
- Tasks depend on `^build` (upstream package builds first)
- TypeScript compilation (`tsc`) runs before Vite build
- Outputs to `dist/` directories

### Path Aliases
- **clips**: `@/` maps to `src/` directory
- **minecraft**: `@/` maps to `src/` directory
- **personal-site**: no path aliases configured

## UI Framework
All apps use Mantine v8 with:
- PostCSS with `postcss-preset-mantine` and `postcss-simple-vars`
- AppShell for layout structure
- Notifications, Modals, and Hooks packages
- Tabler Icons for iconography

## Video Management
Video uploads use:
- Tus protocol for resumable uploads (clips app)
- Bunny CDN for video delivery (`vz-cd8f9809-39a.b-cdn.net`)
- Blurhash for image placeholders
- File hashing utilities in `utils/fileHash.ts` (clips)
