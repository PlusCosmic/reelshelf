# Dropzone

A React application for file uploads and management using Mantine Dropzone.

## Features

- React 19
- Vite for fast development
- Mantine UI components with Dropzone support
- React Query for data fetching
- React Router DOM for routing
- TypeScript
- Shared UI components and services from workspace packages

## Development

```bash
# From the dropzone app directory
pnpm dev

# From the monorepo root
pnpm --filter dropzone dev
```

## Build

```bash
# From the dropzone app directory
pnpm build

# From the monorepo root
pnpm --filter dropzone build
```

## Project Structure

```
dropzone/
├── src/
│   ├── App.tsx          # Main application component
│   ├── main.tsx         # Application entry point
│   └── styles.css       # Global styles
├── index.html           # HTML template
├── vite.config.ts       # Vite configuration
├── tsconfig.json        # TypeScript configuration
├── postcss.config.cjs   # PostCSS configuration for Mantine
└── package.json         # Dependencies and scripts
```

## Available Scripts

- `pnpm dev` - Start development server
- `pnpm build` - Build for production
- `pnpm preview` - Preview production build
- `pnpm lint` - Lint TypeScript files

## Workspace Packages

This app uses shared packages from the monorepo:

- `@repo/ui` - Shared UI components (Footer, UserAvatar, LoginButton)
- `@repo/shared` - Shared business logic and services
- `@repo/nucleus-api-client` - Auto-generated API client
