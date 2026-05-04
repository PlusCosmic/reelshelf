# Game Server Panel Enhancement Roadmap

This document tracks planned feature enhancements for the Minecraft server management panel.

---

## High Priority

### 1. Functional Server Power Controls
**Status:** Completed

The `QuickActions` component now has fully functional power controls.

**Implemented Features:**
- Start/Stop/Restart buttons with real API calls
- Confirmation dialogs before stop/restart with player count warning
- Loading states during container operations
- "Announce to players" option for graceful shutdown
- Automatic status refresh after actions

**API Endpoints:**
- `POST /minecraft/servers/{serverId}/container/start`
- `POST /minecraft/servers/{serverId}/container/stop`
- `DELETE /minecraft/servers/{serverId}/container` (destroy)

---

### 2. Backup Management UI
**Status:** Completed

New Backups page at `/servers/{serverId}/backups`.

**Implemented Features:**
- Backup list with file sizes and modification dates
- Statistics cards (total backups, total size, latest backup, pending sync)
- Manual backup sync trigger button
- Configuration status indicator
- Cyberpunk-themed UI matching the rest of the panel

**API Endpoints:**
- `GET /minecraft/servers/{serverId}/backups`
- `POST /minecraft/servers/{serverId}/backups/sync`

---

### 3. Server Settings Page
**Status:** Completed

New Settings page at `/servers/{serverId}/settings`.

**Implemented Features:**
- Edit server name, MOTD, max players, Minecraft version
- Configure server type (Vanilla/Paper/Fabric/Forge)
- Adjust RAM and CPU reservation/limits
- Modloader version and CurseForge URL configuration
- Server active toggle
- Read-only server information display
- Form validation and dirty state tracking
- Save/Reset buttons with loading states

**API Endpoints:**
- `GET /minecraft/servers/{serverId}`
- `PUT /minecraft/servers/{serverId}`

---

## Medium Priority

### 4. Console Enhancements
**Status:** Planned

**Requirements:**
- Command autocomplete for common Minecraft commands
- Saved/favorite commands quick-access buttons
- Load command history from API (`getCommandHistory` endpoint exists)
- Log level filtering (Info/Warning/Error)
- Search/filter console output
- Export console logs

**API Endpoints:**
- `GET /minecraft/servers/{serverId}/console/history`

---

### 5. Player Management
**Status:** Planned

**Requirements:**
- Click on player to see player details
- Quick actions: Kick, Ban, Op, Teleport
- Player statistics (playtime, last login)
- Whitelist management
- Banned players list

---

### 6. Historical Metrics & Charts
**Status:** Planned

The current metrics show only real-time data.

**Requirements:**
- Add a metrics history chart (line graph of CPU/RAM over time)
- TPS (Ticks Per Second) monitoring
- Player count history
- Peak usage indicators

---

### 7. Multi-Server Overview Dashboard
**Status:** Planned

**Requirements:**
- Summary view showing all servers at a glance
- Quick status indicators for each server
- Aggregate resource usage
- Quick actions from the overview

---

## Low Priority

### 8. Activity/Event Log
**Status:** Planned

**Requirements:**
- Show recent server events (starts, stops, player joins/leaves)
- Command execution history
- Filter by event type
- Timestamp-based browsing

---

### 9. World Management
**Status:** Planned

**Requirements:**
- List worlds on the server
- Download world as ZIP
- Upload world files
- Switch active world
- Reset/regenerate world

---

### 10. Plugin Manager (Paper servers only)
**Status:** Planned

**Requirements:**
- List installed plugins with versions
- Enable/disable plugins
- Plugin configuration editor
- Update notifications

---

### 11. Server Templates/Presets
**Status:** Planned

**Requirements:**
- Pre-configured server templates (Survival, Creative, PvP, etc.)
- Save current configuration as template
- Quick-start from template

---

### 12. Mobile UX Improvements
**Status:** Planned

**Requirements:**
- Bottom navigation for mobile
- Swipe gestures in file browser
- Simplified console view for mobile

---

## Changelog

| Date | Enhancement | Status |
|------|-------------|--------|
| 2025-12-27 | Server Power Controls | Completed |
| 2025-12-27 | Backup Management UI | Completed |
| 2025-12-27 | Server Settings Page | Completed |
