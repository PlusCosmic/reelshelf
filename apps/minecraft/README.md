# Minecraft Server Management App

A React-based web application for managing and monitoring a Minecraft server.

## Features

### Dashboard
- **Server Status Card**: Displays real-time server status (online/offline), player count, max players, MOTD, and version
- **Player List**: Shows currently online players with their usernames and avatars (using Crafatar API)

### Console
- Terminal interface for executing server commands
- Command history with timestamps
- Real-time command execution (when backend is implemented)

### File Browser
- Browse server files and directories
- View file metadata (size, modification date)
- Edit and delete files (when backend is implemented)

## Tech Stack

- **React 19** - UI framework
- **Vite 7.1** - Build tool and dev server
- **TanStack Router** - File-based routing with auto code splitting
- **TanStack Query** - Data fetching and caching with automatic polling
- **Mantine v8** - UI component library
- **Tabler Icons** - Icon library
- **TypeScript** - Type safety

## Project Structure

```
apps/minecraft/
├── src/
│   ├── components/
│   │   ├── layout/
│   │   │   └── AppShell.tsx         # Main layout with navbar and header
│   │   ├── dashboard/
│   │   │   ├── ServerStatusCard.tsx # Server status display
│   │   │   └── PlayerList.tsx       # Online players list
│   │   ├── console/
│   │   │   └── ConsoleTerminal.tsx  # Command terminal
│   │   └── files/
│   │       └── FileBrowser.tsx      # File browser table
│   ├── hooks/
│   │   ├── useServerStatus.ts       # React Query hook for server status
│   │   └── useWebSocket.ts          # WebSocket connection hook
│   ├── services/
│   │   └── minecraft.ts             # API service functions
│   ├── types/
│   │   └── minecraft.ts             # TypeScript interfaces
│   ├── routes/
│   │   ├── __root.tsx               # Root layout
│   │   ├── index.tsx                # Dashboard page
│   │   ├── console.tsx              # Console page
│   │   └── files.tsx                # File browser page
│   ├── main.tsx                     # App entry point
│   └── routeTree.gen.ts             # Auto-generated route tree
├── package.json
├── vite.config.ts
├── tsconfig.json
└── index.html
```

## API Integration

The app connects to the Nucleus API at `https://nucleus.pluscosmic.dev` (configurable via `VITE_API_BASE_URL` env var).

### Expected API Endpoints

- `GET /minecraft/status` - Server status and player info
- `GET /minecraft/players` - List of online players
- `POST /minecraft/console/command` - Execute server command
- `GET /minecraft/files?path=<path>` - List directory contents
- `GET /minecraft/files/content?path=<path>` - Get file content
- `PUT /minecraft/files/content?path=<path>` - Save file content
- `DELETE /minecraft/files?path=<path>` - Delete file

## Development

```bash
# Install dependencies (from monorepo root)
pnpm install

# Start dev server
cd apps/minecraft
pnpm dev

# Build for production
pnpm build
```

## Shared Packages

The app uses the following workspace packages:

- `@repo/shared` - Shared utilities, services, and theme configuration
- `@repo/ui` - Shared UI components (UserAvatar, Footer)

## Features & Enhancements

### Current Features
- Real-time server status monitoring (30-second polling)
- Player list with avatar support
- Console command interface (placeholder)
- File browser UI (placeholder)
- Dark theme with glassmorphic design
- Responsive layout

### Planned Enhancements
- WebSocket connection for real-time console output
- File editing with syntax highlighting
- Server start/stop controls
- Player management (kick, ban, whitelist)
- Server backup and restore
- Plugin management
- Performance metrics and graphs
- Log viewer with search and filtering

## Notes

- The Console and File Browser components are currently placeholders with mock data
- Actual functionality will be enabled once the backend API endpoints are implemented
- The app uses the nucleusTheme from `@repo/shared` for consistent styling
- TanStack Router auto-generates the route tree on build
