# Collaborative Playlists - Design Document

## Overview

A playlist system that allows multiple users to collaboratively build collections of video clips and watch them together. Primary use case: After gaming sessions, players can combine clips from different perspectives into a shared playlist for group viewing.

## Core Use Case Flow

1. **Post-Game Session**: Players finish an Apex Legends match
2. **Clip Creation**: Each player creates/uploads clips from their perspective
3. **Playlist Creation**: One player creates a playlist (e.g., "Apex Session - Jan 17")
4. **Collaboration**: Share playlist with teammates who add their clips
5. **Group Viewing**: Everyone gathers at one computer to watch all clips together

## Requirements

### Functional Requirements

**Playlist Management**
- Create playlists with name and description
- Permanently viewable by all collaborators
- Explicit collaborator invites (no anonymous access)
- Private visibility only (no unlisted/public sharing)
- Simple permissions: All collaborators have equal rights (add/remove clips, invite others, delete playlist)

**Clip Organization**
- Add clips to playlists
- Remove clips from playlists
- Reorder clips within playlist
- "Create playlist from these clips" quick action from filtered clip views

**Smart Playlists / Auto-Grouping**
- Automatically group clips by creation date
- Group by game session metadata (if available)
- Personal "smart collections" (e.g., "My clips from Jan 17")
- Can be used individually or collaboratively

**Playback**
- Sequential playback of all clips in playlist
- Standard video controls (play/pause/skip)
- Auto-advance to next clip
- Visual indicator of current position in playlist

### Non-Functional Requirements

- Fast playlist loading (pagination if needed for large playlists)
- Responsive UI for playlist management
- Clear visual feedback for collaborative actions
- Mobile-friendly playlist viewing

## Data Model

### Database Schema

```sql
-- Playlists table
CREATE TABLE playlists (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name VARCHAR(255) NOT NULL,
  description TEXT,
  creator_user_id UUID NOT NULL REFERENCES users(id),
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);

-- Playlist collaborators (including creator)
CREATE TABLE playlist_collaborators (
  playlist_id UUID REFERENCES playlists(id) ON DELETE CASCADE,
  user_id UUID REFERENCES users(id),
  added_at TIMESTAMP DEFAULT NOW(),
  added_by_user_id UUID REFERENCES users(id),
  PRIMARY KEY (playlist_id, user_id)
);

-- Clips in playlists
CREATE TABLE playlist_clips (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  playlist_id UUID REFERENCES playlists(id) ON DELETE CASCADE,
  clip_id UUID REFERENCES clips(id) ON DELETE CASCADE,
  position INTEGER NOT NULL, -- for ordering
  added_by_user_id UUID REFERENCES users(id),
  added_at TIMESTAMP DEFAULT NOW(),
  UNIQUE(playlist_id, clip_id) -- prevent duplicate clips in same playlist
);

-- Smart playlist definitions (optional for auto-grouping)
CREATE TABLE smart_playlists (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name VARCHAR(255) NOT NULL,
  user_id UUID REFERENCES users(id), -- NULL for global smart playlists
  criteria JSONB NOT NULL, -- filter criteria (date range, tags, etc.)
  created_at TIMESTAMP DEFAULT NOW()
);
```

### Indexes

```sql
CREATE INDEX idx_playlists_creator ON playlists(creator_user_id);
CREATE INDEX idx_playlist_collaborators_user ON playlist_collaborators(user_id);
CREATE INDEX idx_playlist_clips_playlist ON playlist_clips(playlist_id);
CREATE INDEX idx_playlist_clips_position ON playlist_clips(playlist_id, position);
CREATE INDEX idx_smart_playlists_user ON smart_playlists(user_id);
```

### User Preferences (for Discord notifications)

```sql
-- Add to existing users table
ALTER TABLE users ADD COLUMN discord_notifications_enabled BOOLEAN DEFAULT TRUE;
```

## Discord Integration

### Overview

Notifications are delivered via Discord DMs instead of in-app notifications, aligning with the gaming workflow where users are already coordinating on Discord.

### Architecture

**Leverages Existing Infrastructure:**
- Discord OAuth already implemented for login
- `discord_user_id` already stored in users table
- Just need to add Discord bot service to Nucleus API

**Discord Bot Service:**
- Simple DM sender (no bot commands needed for MVP)
- Sends notifications when users are added as collaborators
- Respects user notification preferences

### Notification Flow

1. User A invites User B to playlist
2. Backend checks if User B has `discord_notifications_enabled = true`
3. If enabled, Discord bot sends DM to User B's Discord account
4. Message includes playlist name, inviter, and direct link to playlist

### Message Format

```
🎮 Plus Cosmic Clips

@pluscosmic added you to "Apex Session - Jan 17"

View Playlist: https://clips.pluscosmic.dev/playlists/abc-123

───────────────────────────────────────
You can disable these notifications in your account settings.
```

### User Preferences

**Opt-Out Model:**
- Users can disable Discord notifications in settings
- No in-app notification fallback (keeps it simple)
- Users who opt out rely on manual sharing (Discord chat, etc.)

**Settings UI:**
- Toggle: "Receive Discord notifications for playlist invites"
- Warning text: "If disabled, you won't be notified when added to playlists"

### Implementation Requirements

**One-Time Setup:**
1. Create Discord bot in Developer Portal
2. Add bot token to Nucleus API environment variables
3. Bot needs `Send Messages` permission (for DMs)

**Backend Changes:**
- Discord service module (`services/discord.ts`)
- Hook into `POST /api/playlists/:id/collaborators` endpoint
- User preferences endpoint: `PATCH /api/users/me/preferences`

**Frontend Changes:**
- Settings page: Discord notifications toggle
- Calls preferences API on toggle change

### Edge Cases

**User has notifications disabled:**
- No DM sent
- User relies on manual sharing (Discord chat, etc.)

**Discord DM fails (user has DMs disabled, bot blocked, etc.):**
- Log error but don't fail the API request
- Collaborator is still successfully added
- Consider: Store failed notification attempts for retry/debugging

**User not linked to Discord:**
- Should not happen (OAuth required for login)
- Gracefully handle: skip notification, log warning

## API Design

### Playlists

```
POST   /api/playlists
  Body: { name, description?, visibility? }
  Returns: Created playlist with creator as first collaborator

GET    /api/playlists
  Query: ?mine=true (playlists I created or collaborate on)
  Returns: List of playlists

GET    /api/playlists/:id
  Returns: Playlist details + clips (ordered by position) + collaborators

PUT    /api/playlists/:id
  Body: { name?, description? }
  Returns: Updated playlist

DELETE /api/playlists/:id
  Auth: Must be a collaborator
  Returns: 204 No Content
```

### Playlist Clips

```
POST   /api/playlists/:id/clips
  Body: { clipId } or { clipIds: [] } for batch add
  Returns: Updated playlist with new clip(s)
  Note: Auto-assigns position as last in playlist

DELETE /api/playlists/:id/clips/:clipId
  Returns: 204 No Content

PUT    /api/playlists/:id/clips/reorder
  Body: { clipId, newPosition } or { clipOrdering: [id1, id2, ...] }
  Returns: Updated playlist
```

### Collaborators

```
POST   /api/playlists/:id/collaborators
  Body: { userId } or { email } or { username }
  Returns: Updated collaborator list
  Side Effect: Sends Discord DM notification if user has notifications enabled

DELETE /api/playlists/:id/collaborators/:userId
  Auth: Must be a collaborator (can remove anyone including self)
  Returns: 204 No Content

GET    /api/playlists/:id/collaborators
  Returns: List of collaborators with metadata
```

### Smart Playlists (Future)

```
POST   /api/smart-playlists
  Body: { name, criteria: { dateRange?, tags?, gameMode? } }
  Returns: Smart playlist definition

GET    /api/smart-playlists/:id/clips
  Returns: Clips matching criteria (dynamically generated)

POST   /api/smart-playlists/:id/materialize
  Converts smart playlist to regular playlist
  Returns: New playlist with current matching clips
```

### User Preferences

```
GET    /api/users/me/preferences
  Returns: User preferences including discord_notifications_enabled

PATCH  /api/users/me/preferences
  Body: { discordNotificationsEnabled: boolean }
  Returns: Updated preferences
```

## Frontend Design

### Routes (TanStack Router)

```
/playlists/index.tsx
  - List all playlists (tabs: My Playlists, Shared with Me, Smart Playlists)
  - "Create Playlist" button
  - Search/filter playlists

/playlists/$playlistId/index.tsx
  - Playlist player interface
  - Current clip video player
  - Playlist queue sidebar
  - Collaborator avatars
  - Controls: skip, reorder, remove clips

/playlists/$playlistId/edit.tsx
  - Manage playlist details
  - Invite collaborators
  - Bulk clip operations
  - Delete playlist

/playlists/new.tsx
  - Create new playlist form
  - Optional: import clips from current filter/search
```

### Key Components

**`PlaylistPlayer`**
- Video player with auto-advance
- Current clip info overlay
- Progress through playlist (e.g., "3 / 12 clips")
- Next/previous controls

**`PlaylistQueue`**
- Draggable clip list for reordering
- Remove clip button
- Current clip highlight
- Jump to clip on click

**`PlaylistCard`**
- Playlist preview (first clip thumbnail)
- Name, clip count, collaborator avatars
- Last updated timestamp

**`CreatePlaylistModal`**
- Quick create from anywhere
- Name + description
- Option to add current filtered clips
- Invite collaborators immediately

**`AddToPlaylistButton`**
- Appears on clip cards
- Dropdown: existing playlists or "Create new"
- Quick add to recently used playlists

### UX Patterns

**Quick Actions**
- Clip view → "Add to playlist" → Select or create
- Filtered clips → "Create playlist from these N clips"
- Playlist → "Share" → Copy link or invite by username

**Bulk Operations**
- Multi-select clips to add multiple at once
- Batch remove from playlist
- Drag-and-drop reordering

**Smart Features**
- Recently used playlists at top of "Add to playlist"
- Suggested collaborators (recent gaming partners)
- Auto-naming: "Session - [Date]" or "[Game Mode] - [Date]"

## Implementation Phases

### Phase 1: MVP (Core Playlist Functionality)

**Backend:**
- [ ] Database schema (playlists, playlist_collaborators, playlist_clips)
- [ ] Basic CRUD endpoints for playlists
- [ ] Add/remove clips endpoints
- [ ] Collaborator invite/remove endpoints
- [ ] Authorization middleware (check collaborator status)
- [ ] Discord bot setup (one-time: create bot, get token)
- [ ] Discord service module (send DM function)
- [ ] Add `discord_notifications_enabled` to users table
- [ ] User preferences endpoints (GET/PATCH)
- [ ] Hook Discord notifications into collaborator creation

**Frontend:**
- [ ] Playlists list route
- [ ] Create playlist modal
- [ ] "Add to playlist" button on clip cards
- [ ] Basic playlist player (sequential playback)
- [ ] Playlist queue sidebar
- [ ] User settings: Discord notifications toggle

**Testing:**
- [ ] API endpoint tests
- [ ] Discord notification integration test
- [ ] Playlist player Playwright tests
- [ ] Collaborative workflow test (multi-user)

### Phase 2: Enhanced Features

**Backend:**
- [ ] Reorder clips endpoint
- [ ] Playlist search/filter
- [ ] Activity feed (who added what clip)

**Frontend:**
- [ ] Drag-and-drop reordering
- [ ] "Create from filtered clips" quick action
- [ ] Playlist sharing UI (copy link)
- [ ] Collaborator management UI
- [ ] Playlist statistics (total duration, clip count, etc.)

### Phase 3: Smart Playlists

**Backend:**
- [ ] Smart playlist schema and endpoints
- [ ] Dynamic clip filtering based on criteria
- [ ] Materialize smart playlist to regular playlist

**Frontend:**
- [ ] Smart playlist creation UI
- [ ] Criteria builder (date range, tags, game mode)
- [ ] Preview clips before creating
- [ ] Convert to regular playlist action

### Phase 4: Polish & Advanced Features

**Frontend:**
- [ ] Playlist thumbnails (auto-generated from clips)
- [ ] Keyboard shortcuts in player (space = play/pause, arrows = skip)
- [ ] Mini-player mode
- [ ] Download entire playlist option

**Backend:**
- [ ] Playlist analytics (view counts, popular clips)
- [ ] Duplicate playlist feature
- [ ] Playlist templates

## Technical Considerations

### Performance

- **Pagination**: For playlists with 50+ clips, implement virtual scrolling or pagination
- **Caching**: Cache playlist metadata and clip lists in React Query
- **Lazy Loading**: Load clip metadata on-demand in player

### Security

- **Authorization**: All endpoints check if user is collaborator
- **Input Validation**: Validate clip IDs exist and user has access
- **Rate Limiting**: Prevent spam playlist creation

### Edge Cases

- **Deleted Clips**: Handle gracefully in playlist (show placeholder or auto-remove)
- **Duplicate Clips**: Prevent adding same clip twice to playlist
- **Concurrent Edits**: Last-write-wins for reordering (acceptable for this use case)
- **Empty Playlists**: Allow but show helpful "Add clips to get started" message

## Design Decisions

1. **Playlist Deletion**: ✅ Any collaborator can delete playlist (simple permission model)

2. **Visibility Options**: ✅ Private only - must explicitly add collaborators (no unlisted/public)

3. **Notifications**: ✅ Discord DM notifications when added as collaborator
   - Opt-out via user preferences (no in-app fallback)
   - No notifications for individual clip additions (avoid noise)

4. **Clip Limits**: ✅ No hard limit, but may warn at 100+ clips for UX

5. **Smart Playlist Scope**: ✅ Start user-specific, can materialize to regular playlist for sharing

## Success Metrics

- Number of playlists created per user
- Average clips per playlist
- Average collaborators per playlist
- Playlist playback completion rate
- Time from creation to first playback

## Future Enhancements

- **Comments**: Add comments/timestamps to specific clips in playlist
- **Reactions**: React to clips during playback (emoji overlay)
- **Playlist Merging**: Combine two playlists
- **Public Playlists**: Share playlists publicly (read-only)
- **Embed Player**: Embed playlist player on external sites
- **Mobile App**: Dedicated mobile playlist viewer
