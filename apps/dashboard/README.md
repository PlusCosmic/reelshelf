# Dashboard

Administrative dashboard application built with React, React Router, and React Query, featuring comprehensive testing with Vitest and Playwright.

## Features

- React Router for navigation
- React Query for server state management
- Mantine UI component library with forms, modals, and notifications
- Auto-generated API client integration with Nucleus backend
- OpenAPI integration with openapi-fetch and openapi-react-query
- Cloudflare Workers support with Wrangler
- Comprehensive testing setup:
  - Unit tests with Vitest
  - E2E tests with Playwright
  - Mock Service Worker (MSW) for API mocking
  - Coverage reporting with V8

## Tech Stack

- **React 19** - UI framework
- **React Router** - Client-side routing
- **React Query** - Server state management
- **Mantine** - Component library with forms, modals, and notifications
- **Vite** - Build tool and dev server
- **TypeScript** - Type safety
- **Vitest** - Unit testing framework
- **Playwright** - E2E testing
- **MSW** - API mocking for tests

## Development

```bash
# Start development server
pnpm dev

# Build for production
pnpm build

# Preview production build
pnpm preview

# Lint code
pnpm lint

# Run unit tests
pnpm test

# Run tests in watch mode
pnpm test:watch

# Run E2E tests
pnpm e2e

# Run E2E tests with UI
pnpm e2e:ui
```

## Environment Variables

Configure environment variables in `.env.development` and `.env.production` files.

## API Client

This app uses auto-generated API clients from OpenAPI specifications. The Nucleus API client is provided by the `@repo/nucleus-api-client` workspace package.
