# Clips

A video/media clip management application built with React, TanStack Router, and Mantine UI.

## Features

- File-based routing with TanStack Router
- Media upload with tus-js-client for resumable uploads
- Dropzone integration for drag-and-drop file uploads
- Blurhash integration for progressive image loading
- Mantine UI component library with custom theming
- Date handling with dayjs
- Tag management for clips

## Tech Stack

- **React 19** - UI framework
- **TanStack Router** - Type-safe routing with devtools
- **Mantine** - Component library with core, dates, dropzone, hooks, and notifications
- **Vite** - Build tool and dev server
- **TypeScript** - Type safety
- **Sass** - Styling
- **tus-js-client** - Resumable file uploads

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
```

## Environment Variables

Configure environment variables in `.env.development` and `.env.production` files.
