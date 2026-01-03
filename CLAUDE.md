# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Plus Cosmic Development is a Turborepo monorepo containing video clip management applications and shared packages. The project uses Bun workspaces with Vite for fast development builds.

**Backend**: Nucleus API (separate backend service at nucleus.pluscosmic.dev)
**Main Branch**: `develop` (use this for PRs, not `main`)

## Applications

### clips
Video/media clip management application using TanStack Router for file-based routing. Routes are auto-generated from `src/routes/` directory structure via the TanStack Router plugin.

**Key Features**:
- File-based routing with `__root.tsx` providing AppShell layout
- Video uploads using tus-js-client for resumable uploads
- Uses Mantine UI components with modals and notifications
- TanStack Router DevTools and React DevTools integrated in footer

**Route Structure**: Routes in `src/routes/` auto-generate `routeTree.gen.ts`. The `__root.tsx` provides the AppShell layout wrapper for all routes.

### home
Homepage application using React Router DOM (not TanStack Router like clips). Single-page application centered around the Home page.

**Key Features**:
- React Query for data fetching and caching
- Playwright for E2E testing
- MSW (Mock Service Worker) for API mocking in tests
- Weather integration and Apex Legends game data

## Packages

### @repo/nucleus-api-client
**Auto-generated API client** from OpenAPI specification. Never manually edit the `src/` directory.

**Regeneration Process** (from `packages/nucleus-api-client/`):
1. Clear the `src/` directory
2. Run: `bunx openapi-generator-cli generate -i Nucleus.json -g typescript-fetch -o src --additional-properties=supportsES6=true`
3. Run: `bun run build`

The `Nucleus.json` OpenAPI spec must be updated before regenerating the client.

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
- `services/apexClips.ts` - Video clip operations (fetch, create, tag, delete, view tracking)
- `services/ffmpeg.ts` - Video download functionality

**API Configuration**: Uses `VITE_API_BASE_URL` environment variable, defaults to `https://nucleus.pluscosmic.dev`

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

# Individual app development (from app directory)
cd apps/clips
bun run dev

cd apps/home
bun run dev

# Install a package in a specific workspace
bun add <package> --cwd apps/<app-name>
```

## Testing

### Home App
```sh
cd apps/home

# Unit tests (vitest)
bun run test
bun run test:watch

# E2E tests (playwright)
bun run e2e
bun run e2e:ui
```

**Test Setup**:
- Vitest config in `package.json` (not separate file)
- Setup file: `src/setupTests.ts`
- E2E tests in `tests/` directory (excluded from vitest)
- MSW for API mocking

### Clips App
Uses vitest but configuration is in `package.json`, not a separate config file.

### Testing Visual/Frontend Changes
**IMPORTANT**: When making visual or frontend changes in TypeScript applications (clips or home apps), always test the changes using the Playwright MCP server to verify functionality before completing the task. This ensures UI changes work correctly in a real browser environment.

## Architecture Notes

### Routing Differences
- **clips**: TanStack Router with file-based routing, auto-code splitting enabled
- **home**: React Router DOM with traditional route configuration

### State Management
- **clips**: React state + TanStack Router state
- **home**: React Query for server state, React state for local state

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
- **home**: No path aliases configured

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
