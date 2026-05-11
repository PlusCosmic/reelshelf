# Reelshelf

Reelshelf is a personal clip library for storing, organizing, and sharing gameplay clips.

## Language

**Clip**:
A gameplay video saved in the library.

**Library**:
The user's browsable archive of saved clips.

**Playlist**:
A user-curated set of clips that the frontend may label as a "collection".
_Avoid_: Using "collection" in API/domain code when referring to this concept.

**Bunny Collection**:
An internal Bunny Stream storage grouping used for uploaded videos.
_Avoid_: Exposing this as a user-facing collection.

**Bulk Upload Queue**:
A temporary review workspace for multiple local clip files before they are saved as clips.

**Game Assignment**:
The selected game category for a queued file or saved clip.

**Source Path**:
The best available browser-provided path for a local file, used only for upload review heuristics.

**Gaming Session**:
A group of clips from the same game within the same 5am-to-5am gaming day, represented after upload as an auto-generated **Playlist**.

## Relationships

- A **Bulk Upload Queue** contains one or more local files that may become **Clips**.
- A **Clip** belongs to the **Library**.
- A **Clip** may belong to zero or more **Playlists**.
- A **Bunny Collection** stores uploaded video assets but is not the same as a **Playlist**.
- A **Gaming Session** is represented as an auto-generated **Playlist** after queued clips are saved.
- A **Gaming Session** playlist may be renamed or manually edited without changing the underlying session identity.
- The session collection endpoint ensures submitted clips are present in the **Gaming Session** playlist, even if they had been manually removed before.
- A bulk-uploaded **Clip** can be added to both its automatic **Gaming Session** playlist and a user-selected **Playlist**.
- The upload flow updates a **Gaming Session** playlist after active uploads for that session finish; later retries can add newly successful clips.
- Duplicate upload detection is based on owner, game assignment, and file fingerprint.
- Tags selected during bulk upload are queue metadata until the corresponding **Clip** is successfully saved.
- Committing a **Bulk Upload Queue** uploads selected rows only; unselected rows remain in the queue.
- A queued row is saved only after the video upload and post-upload filing both succeed.
- Dropping files creates a **Bulk Upload Queue**; files are not saved as **Clips** until the user commits the queue.
- Single-file and multi-file uploads both use the **Bulk Upload Queue**.
- **Game Assignment** in the **Bulk Upload Queue** is inferred from filename/folder names first; a default game is only applied when upload starts from a game page.
- **Source Path** falls back from browser-provided relative path to filename; directory support is helpful but not required.
- Bulk upload supports normal file selection, browser-supported folder selection, and browser-supported folder drag/drop.
- Bulk upload runs at most three active uploads at a time.
- Bulk upload can pause/resume active TUS uploads per row or pause/resume the queue globally.
- Leaving a **Bulk Upload Queue** with unsaved rows or active uploads should warn the user; local files are not recovered after refresh.
- Fully saved **Gaming Sessions** collapse into queue summaries that link to their collection; unresolved rows stay visible.
- Saved upload macros are deferred; bulk tag, collection, and selection actions are the underlying primitives.
- Title regeneration is deferred; queued clips use filename-derived titles with inline editing.
- Clip splitting is deferred and should be treated as a later clip editing feature.
- API contract changes are consumed through the generated TypeScript API client.
- A **Gaming Session** uses the clip timestamp from file metadata first and filename patterns second.
- The **Gaming Session** 5am boundary is evaluated in the uploader's browser-local timezone for upload review.
- A **Gaming Session** identity uses owner, game assignment, and a plain gaming-day date; timezone may be stored as context.

## Example dialogue

> **Dev:** "When the upload UI says add these clips to a collection, should that create a Bunny Collection?"
> **Domain expert:** "No. In the UI, collection means the user-facing **Playlist** concept. **Bunny Collection** is internal storage language."
>
> **Dev:** "Does dropping files onto the library immediately save clips?"
> **Domain expert:** "No. Dropping files opens a **Bulk Upload Queue** for review; the user commits the queue when ready."
>
> **Dev:** "Does a single clip use a different upload workflow?"
> **Domain expert:** "No. A single file is just a one-row **Bulk Upload Queue**."
>
> **Dev:** "Should every queue have a default game?"
> **Domain expert:** "No. Prefer filename and folder inference. Only default the game when the upload begins from a specific game page."
>
> **Dev:** "If upload starts from a game page, does that override filename inference?"
> **Domain expert:** "No. The game page supplies fallback context only for files that do not match by source path."
>
> **Dev:** "Do folder heuristics require full dropped-directory support?"
> **Domain expert:** "No. Use the best available **Source Path**, and fall back to the filename when the browser gives no folder path."
>
> **Dev:** "Should upload have an explicit folder picker?"
> **Domain expert:** "Yes, where the browser supports it; normal multi-file selection remains the fallback."
>
> **Dev:** "Can users configure bulk upload concurrency?"
> **Domain expert:** "No. Start with a fixed limit of three active uploads."
>
> **Dev:** "Does pause stop hashing or clip creation?"
> **Domain expert:** "No. Pause applies to TUS uploads and to starting more queued rows."
>
> **Dev:** "Can users refresh and recover unsaved queued files?"
> **Domain expert:** "No. Warn before leaving, but do not persist local file handles in this implementation."
>
> **Dev:** "Do saved rows disappear from the upload queue?"
> **Domain expert:** "No. Fully saved sessions collapse into summaries; unresolved rows stay expanded."
>
> **Dev:** "Are saved macros part of the first bulk upload implementation?"
> **Domain expert:** "No. Build the bulk actions first and leave macros as an extension."
>
> **Dev:** "Should the row action regenerate clip titles?"
> **Domain expert:** "No. Use filename-derived titles and inline editing until title generation is a real feature."
>
> **Dev:** "Should the bulk upload row action split clips?"
> **Domain expert:** "No. Splitting is a separate clip editing feature, not part of first bulk upload."
>
> **Dev:** "Can the frontend call new API endpoints by hand?"
> **Domain expert:** "No. Regenerate the API client and use the generated contract."
>
> **Dev:** "Are two Apex clips from 11pm and 2am different sessions?"
> **Domain expert:** "No. They are in the same **Gaming Session** if they fall in the same 5am-to-5am gaming day."
>
> **Dev:** "Which timezone defines that 5am boundary?"
> **Domain expert:** "Use the uploader's browser-local timezone while reviewing the upload queue."
>
> **Dev:** "Does the backend session endpoint need window start and end instants?"
> **Domain expert:** "No. Send a plain gaming-day date plus timezone; the date is the durable session identity."
>
> **Dev:** "Is a session just a visual group in the upload queue?"
> **Domain expert:** "No. A **Gaming Session** becomes an auto-generated **Playlist**."
>
> **Dev:** "Can the frontend create session collections by matching playlist names?"
> **Domain expert:** "No. Use a backend endpoint so one owner/game/gaming-day session maps to one auto-generated **Playlist**."
>
> **Dev:** "If I rename an auto-generated session collection, should future uploads create a new one?"
> **Domain expert:** "No. The **Playlist** display name is editable, but the underlying **Gaming Session** identity remains stable."
>
> **Dev:** "If a clip was manually removed from a session collection, can the upload endpoint add it again?"
> **Domain expert:** "Yes. Submitted clips are ensured present; we do not track removal tombstones for session collections."
>
> **Dev:** "Does selecting a collection replace the automatic gaming session collection?"
> **Domain expert:** "No. A clip can be added to both; they are independent memberships."
>
> **Dev:** "Do we update the session collection after every individual clip finishes?"
> **Domain expert:** "No. Update it after the active uploads for that session finish, then update again only for later successful retries."
>
> **Dev:** "If a file is already uploaded, do we add the existing clip to the new session collection?"
> **Domain expert:** "Not unless the backend returns the existing clip identity. A duplicate row is left unsaved."
>
> **Dev:** "Do tags get created before upload?"
> **Domain expert:** "No. Tags selected in the **Bulk Upload Queue** are applied only after a **Clip** is saved."
>
> **Dev:** "Does committing selected clips discard unselected files?"
> **Domain expert:** "No. Unselected files remain in the **Bulk Upload Queue** until the user removes them or leaves."
>
> **Dev:** "If the video uploads but adding it to collections fails, is the row saved?"
> **Domain expert:** "No. The video exists, but the row needs filing retry before it is considered saved."

## Flagged ambiguities

- "collection" can mean both frontend collection UI and Bunny storage collection. Resolved: frontend "collection" maps to **Playlist**; **Bunny Collection** is internal-only.
