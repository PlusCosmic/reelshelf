# Plus Cosmic Development

A monorepo containing Plus Cosmic applications and shared packages, built with Turborepo and Vite.

## Applications

- **clips** - Video/media clip management application with TanStack Router
- **dashboard** - Administrative dashboard with React Query and routing

## Packages

- `@repo/nucleus-api-client` - Auto-generated API client for Nucleus backend
- `@repo/shared` - Shared utilities and components
- `@repo/ui` - UI component library
- `@repo/eslint-config` - Shared ESLint configurations
- `@repo/typescript-config` - Shared TypeScript configurations

## Development

This monorepo uses:
- [pnpm](https://pnpm.io/) for package management
- [Turborepo](https://turbo.build/) for build orchestration
- [Vite](https://vitejs.dev) for fast development and builds
- [TypeScript](https://www.typescriptlang.org/) for type safety

### Commands

```sh
# Run all apps in development mode
pnpm dev

# Build all apps and packages
pnpm build

# Lint all packages
pnpm lint

# Format code
pnpm format
```
