# Plus Cosmic Development

A Turborepo monorepo with full-stack services using .NET APIs and React frontends.

## Services

Each service contains an `api/` (C# .NET) and `frontend/` (React + Vite).

- **clips** - Video clip management with resumable uploads, Apex Legends integration, and Bunny CDN
- **minecraft** - Minecraft server tools with Monaco editor for configuration

## Packages

### TypeScript
- `@repo/nucleus-api-client` - Auto-generated TypeScript client for Nucleus API
- `@repo/shared` - Shared services and hooks
- `@repo/ui` - Mantine-based UI components
- `@repo/eslint-config` - ESLint configurations
- `@repo/typescript-config` - TypeScript configurations

### .NET
- `Nucleus.Shared` - Shared .NET library (Auth, Discord, Games)

## Tech Stack

**Frontend**: React 19, TanStack Router, TanStack Query, Mantine, Vite
**Backend**: .NET, Dapper, Npgsql, PostgreSQL
**Tooling**: Bun, Turborepo

## Getting Started

```sh
# Install frontend dependencies
bun install

# Run all frontends in development
bun run dev

# Build all frontends
bun run build

# Lint
bun run lint

# Typecheck and tests
bun run typecheck
bun run test

# Build .NET APIs
bun run build:api
```

For .NET APIs, use `dotnet run` from the respective `services/*/api/` directories.
Database schema changes live in `migrations/` and should be applied in filename order.
