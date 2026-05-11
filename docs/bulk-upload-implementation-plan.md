# Bulk Upload Implementation Plan

## Source Designs

The bulk upload flow is documented in `docs/UI Design/bulk-upload.jsx` as two connected surfaces:

- `LibraryDropEntry`: a drag-over overlay on the library that lets a user drop a folder or multiple clip files directly onto the shelf.
- `BulkUploadQueue`: a dense queue page for reviewing many clips before saving them, with session grouping, row selection, inline metadata edits, bulk tags, collection filing, macros, per-row game override, progress, and a sticky summary/action footer.

This plan treats the design as the target UX, but stages implementation around the current app constraints: uploads are already Bunny TUS uploads prepared one clip at a time through `POST /api/clips/categories/{categoryId}/videos`; tags and playlist filing happen through separate APIs; there is no general game-detection API today.

## Current Baseline

Frontend:

- `frontend/src/routes/upload.tsx` hosts the current single-file upload route.
- `frontend/src/hooks/useClipUpload.ts` owns one selected `File`, hashes it, calls `useCreateVideo`, then uploads to Bunny with `tus-js-client`; this should be replaced by the queue engine rather than kept as a parallel workflow.
- Upload UI is split across `frontend/src/components/Upload/*`.
- The library route is `frontend/src/routes/index.tsx`; it can become the drag/drop entry point.
- Categories are already available through `useCategories` / `useLibraryData`.
- Playlists can be listed and clips can be added in bulk through `addClipsToPlaylist`.

Backend/API:

- `api/Core/ClipsEndpoints.cs` exposes single-video create, title update, tag add/remove, delete, and library endpoints.
- `api/Core/ClipService.cs` creates one Bunny video and one local `clip` row per request.
- Duplicate detection is per user + game category + MD5 hash.
- Tags are limited to 5 per clip.
- Playlists support adding multiple existing clip IDs.
- There is Apex-specific detection infrastructure, but no general "detect game from uploaded video/filename" contract.

## Product Scope

### MVP

Ship a production bulk upload flow that supports:

- Multi-file selection and drag/drop from `/upload`.
- Folder selection where the browser supports directory inputs.
- Single-file selection and drag/drop through the same queue surface.
- Drag/drop overlay from the library page.
- Upload entry from a game page with that game as fallback context.
- A queue table grouped into sessions by game assignment and 5am-to-5am gaming day.
- Per-row title, game assignment, and selected state.
- Inline title editing with filename-derived defaults.
- Bulk tags for selected rows.
- Optional playlist assignment for selected rows.
- Auto-generated session collections/playlists for uploaded rows grouped by game and 5am-to-5am gaming day.
- Automatic session collection membership and user-selected collection membership are independent; apply both when both exist.
- Concurrent upload with a fixed limit of 3 active uploads.
- Per-row progress, pause/resume/cancel where Bunny TUS supports it.
- Global pause/resume for the queue.
- Duplicate/error states that do not block the whole queue.
- Final "Add selected to library" action that uploads selected ready rows and applies post-upload tags/collections.
- Dropping files never creates clips by itself; it only opens a review queue.

### Deferred

These should be designed into state shape but not block the first implementation:

- Saved macros as persistent user objects.
- AI/generated title regeneration.
- True clip splitting.
- General automatic game detection from video content.
- A separate user-facing collection model distinct from playlists.
- Background upload recovery after full page reload.

## UX Flow

1. Entry
   - `/upload` supports both single and multiple files.
   - `/upload` offers a folder picker via browser-supported directory input where available.
   - Library route listens for file drag enter/over and displays the designed dimmed overlay.
   - Game pages can navigate to `/upload` with a category context.
   - Dropping files or folders navigates to `/upload` and passes files through in-memory route state or a shared upload queue store.
   - Dropped files remain local-only until the user confirms the queue.
   - Leaving the route with unsaved rows or active uploads should warn the user.

2. Queue Build
   - Filter to supported video files: MP4, MOV, WebM, MKV.
   - Reject files over the existing 4 GB limit.
- Create one queue row per valid file, including the single-file case.
   - Default title is filename without extension.
   - Default created date uses `file.lastModified`.
   - Infer session grouping from adjacent files by selected/inferred game and timestamp gap.

3. Review
   - Rows are selected by default.
   - Users can edit title and game inline.
   - Rows without a game assignment are `needs_game` and cannot be uploaded until fixed.
   - Bulk bar applies tags and optional user-selected collection to selected rows.
   - Tags remain queue metadata until each clip successfully uploads.
   - Session groups preview the auto-generated collection that will be created for each uploaded session.
   - Sticky footer reports total files, size, duration where known, ready count, uploading count, error count.

4. Commit
   - "Add selected to library" starts upload for selected rows that are ready.
   - Unselected rows remain in the queue with their metadata intact.
   - Each row hashes file, creates Bunny/local clip, performs TUS upload, then applies post-upload tags, optional user-selected collection membership, and auto-generated session collection membership.
   - The queue stays visible until all selected rows are complete or failed.
   - Successful rows transition to `saved`; failed rows stay retryable.
   - Fully saved sessions collapse into summaries with links to the created collection. Failed or unsaved rows remain expanded.

## State Model

Add a new hook/module rather than expanding `useClipUpload` in place:

- `frontend/src/hooks/useBulkClipUpload.ts`
- `frontend/src/components/Upload/BulkUploadQueue.tsx`
- `frontend/src/components/Upload/LibraryDropOverlay.tsx`
- session collection creation can reuse playlist APIs, presented as "collections" in the UI.

Suggested row type:

```ts
type BulkUploadStatus =
  | "ready"
  | "needs_game"
  | "hashing"
  | "creating"
  | "uploading"
  | "paused"
  | "uploaded"
  | "filing"
  | "saved"
  | "filing_error"
  | "duplicate"
  | "error";

type BulkUploadRow = {
  id: string;
  file: File;
  sourcePath: string;
  title: string;
  categoryId: string | null;
  createdAt: Date;
  sessionDate: string;
  tags: string[];
  playlistId: string | null; // Presented as "collection" in the UI.
  md5Hash?: string;
  clipId?: string;
  bunnyVideoId?: string;
  progress: number;
  bytesUploaded: number;
  error?: string;
  status: BulkUploadStatus;
};
```

Keep row state local to the upload route for MVP. Use a lightweight shared store only if the library drop entry needs to pass files across navigation more cleanly than TanStack route state allows.

Add route-leave and `beforeunload` protection when there are unsaved queue rows, active uploads, or uploaded rows with filing still pending. Do not persist local file handles for refresh recovery in this implementation.

Keep a clean extension point for saved macros, but do not implement persisted macros in the first build. The underlying actions they would call are in scope: apply tags, apply a collection, and select/deselect rows or sessions.

Do not implement row-level clip splitting in the first build. Splitting should be treated as a later clip editing feature because it affects video processing, storage, and metadata inheritance.

## Upload Engine

Extract reusable functions from `useClipUpload.ts`:

- file validation
- default title generation
- local datetime conversion
- Bunny TUS upload creation
- upload error normalization

Then retire `useClipUpload` once the queue engine covers the single-file path.

Bulk upload should run a queue with a fixed concurrency limit of 3 active uploads. Each row lifecycle:

1. `calculateFileMD5(file)`
2. `createVideoRequest(categoryId, title, md5Hash, createdAt)`
3. TUS upload with the returned signature/expiration/video/library IDs
4. Add tags through `addTagToVideo`
5. Add clip to the selected collection through `addClipsToPlaylist`, preferably batched after all uploads for a collection complete
6. After a session has no active uploads left, call the session collection endpoint once with that session's successful clip IDs

Important behavior:

- Detect duplicate files inside the queue after MD5 hashes are available. For the same game category + MD5, mark later rows as `duplicate`.
- A duplicate `409` maps to row status `duplicate`.
- Do not add an already-existing duplicate clip to the new session collection unless a future backend response returns the existing clip ID.
- Validate the 5-tag limit in the queue before commit. Apply tags only after the clip successfully exists; failed and duplicate rows should not call tag endpoints.
- A single row failure does not stop other rows.
- A row becomes `uploaded` after Bunny TUS succeeds, `filing` while applying tags/collections, and `saved` only after filing succeeds.
- If post-upload filing fails, keep the uploaded clip identity and show `filing_error`; retry filing without re-uploading the video.
- Cancel should abort the active TUS upload and leave the local row removable.
- Per-row pause/resume applies to active TUS uploads. Global pause/resume pauses active uploads and prevents queued rows from starting. Hashing and clip creation are not pauseable.
- Retry should reuse existing row metadata and recompute/create only when needed.
- Session collection creation runs once per session batch after active uploads for that session finish; later retries call the endpoint again with newly successful clip IDs.
- Committing the queue uploads only selected ready rows. Unselected rows stay local until selected, removed, or the page is left.
- Keep saved rows visible for confirmation, but collapse fully saved sessions into summaries. Keep sessions expanded when they contain failed, duplicate, filing-error, or unsaved rows.

## API Work

### Required

No new clip-upload API is strictly required if the frontend orchestrates per-row operations against existing endpoints.

Add a small backend endpoint for auto-generated session collections so the client does not infer idempotency from playlist names:

- `POST /api/playlists/gaming-sessions`
- Request:
  - `category_id`
  - `session_date` as a plain gaming-day date, for example `2026-05-10`
  - `clip_ids`
  - `timezone`, for example `Europe/Berlin`
- Behavior:
  - resolve the authenticated owner
  - find or create the auto-generated playlist for owner + category + session date
  - ensure the provided clips are present, ignoring duplicates
  - return the playlist
- Suggested generated name: `{Game name} · {Gaming day}`, for example `Apex Legends · May 10, 2026`.

This needs a migration to make idempotency durable and independent of editable playlist display names. Use either an `auto_playlist_key`/`kind` column or a dedicated session playlist table keyed by owner, category, and plain `session_date`; store `timezone` for display/audit if useful. The resulting playlist should remain editable like a normal collection, including name/description and manual clip changes, without changing the underlying session identity. Do not add removal tombstones; if clips are submitted to the endpoint, it ensures they are present.

After adding this endpoint, regenerate and commit the TypeScript API client with `bun run generate:api-client`, consume the generated client from the frontend, and verify drift with `bun run check:api-client-drift`.

Potential small backend improvement:

- Add a batch prepare endpoint only if per-row `CreateVideo` becomes too slow or rate-limit prone:
  - `POST /api/clips/bulk/videos`
  - Request includes rows with `category_id`, `title`, `created_at`, `md5_hash`.
  - Response returns per-row create results or duplicate/error results.

### Likely Follow-up

- Bulk tag endpoint to apply tags to many clip IDs with one request.
- Persisted upload macros:
  - macro name
  - tags
  - collection/playlist ID
  - optional category ID
- General detection endpoint or background job if the design's "Detecting" state becomes real:
  - filename/time/category heuristic can be frontend-only for MVP
  - content-based detection should be explicitly designed as a separate feature

## Game Assignment

MVP should avoid pretending content detection exists.

Use deterministic heuristics:

- Build a `sourcePath` per row from the best available browser metadata: `webkitRelativePath`, drag/drop directory traversal path, then `file.name`.
- If `sourcePath` contains a category slug/name substring, assign that category with a "filename/folder match" confidence.
- If the upload starts from a game page, apply that game as fallback context only to rows that filename/folder heuristics did not match.
- Otherwise mark as `needs_game`.

Render confidence only for known heuristics. Use labels like "Filename match" instead of percentage if there is no real model confidence.

## Session Grouping

Implement grouping as a pure helper:

- Sort rows by `createdAt`, preserving drop order as a fallback.
- Derive `createdAt` from `file.lastModified` first.
- If `file.lastModified` is unavailable or not useful, parse common filename patterns such as `YYYY-MM-DD_HHMM`, `YYYY-MM-DD_HH-MM`, and `YYYYMMDD_HHMMSS`.
- Derive `sessionDate` from a 5am-to-5am gaming-day window.
- Evaluate the 5am boundary in the uploader's browser-local timezone.
- Start a new session when category or `sessionDate` changes.
- Put `needs_game` rows into an unmatched group.
- After upload, create an auto-generated collection/playlist per session and add that session's uploaded clips to it.

This helper should be unit-tested because it drives selection and table structure.

## Styling and Components

Implement the design in existing Reelshelf styles rather than inline styles:

- Keep the current serif display voice and shelf/library visual language.
- Add CSS classes in `frontend/src/styles/reelshelf.css`.
- Use existing primitives where they fit: `Chip`, `SearchBox`, navigation layout.
- Use Tabler icons because the app already uses `@tabler/icons-react`.
- Make the dense table responsive:
  - desktop: full queue grid
  - narrow screens: row cards with title, game, tags, progress, actions

## Implementation Steps

1. Create shared upload helpers extracted from `useClipUpload.ts`.
2. Add the backend session collection endpoint and migration.
3. Run `bun run generate:api-client` and consume the generated endpoint from frontend code.
4. Add `useBulkClipUpload` with row reducer, selection, bulk tag/playlist operations, session grouping, and upload concurrency.
5. Replace `/upload` with the queue surface for both single-file and multi-file uploads; use a less dense one-row presentation if needed.
6. Add library drag/drop overlay in `frontend/src/routes/index.tsx`; on drop, navigate to `/upload` with the file list queued.
7. Add collection picker support to the bulk bar using existing playlist APIs: `fetchPlaylists` and `addClipsToPlaylist`.
8. Create session collections after successful uploads using the new session collection endpoint.
9. Apply tags after upload using existing tag endpoints; batch locally by row.
10. Add focused tests for validation, grouping, row reducer transitions, session collection creation, and concurrency scheduling.
11. Run `bun run typecheck`, `bun run lint`, targeted Vitest tests, and `bun run check:api-client-drift`.

## Testing Plan

Unit tests:

- video file validation accepts supported MIME types and extensions.
- invalid files and >4 GB files are rejected per row.
- session grouping handles category changes and 5am-to-5am gaming-day changes.
- bulk tag operation respects the 5-tag backend limit before upload.
- duplicate detection marks later rows with the same game category + MD5 as duplicates.
- queue runner honors concurrency and continues after row failure.

Integration/manual checks:

- Drop 12 mixed valid files on `/`.
- Drop/browse multiple files on `/upload`.
- Assign games to unmatched rows.
- Apply a tag to selected rows.
- Upload with one intentional duplicate and verify other rows complete.
- Add uploaded clips to an existing collection.
- Verify each uploaded gaming session creates or updates the expected auto-generated collection.
- Pause/resume an active row.

## Open Decisions

- How should auto-generated session collections be named, and should they be idempotent across repeated uploads?
- Should a future version persist/recover upload queue state across browser restarts with File System Access API permissions?

## Recommended First Slice

Start with the backend session collection endpoint because it records the hardest domain decision and gives the frontend a real contract.

First issue: Add auto-generated Gaming Session collection endpoint.

- Add a migration for durable session identity.
- Add an endpoint under playlists.
- Add service logic to find/create the session playlist and ensure submitted clips are present.
- Regenerate the TypeScript API client.
- Test idempotency, editable name independence, and duplicate clip adds.

Second issue: Build bulk queue foundations without real uploading.

- Add the queue row model/reducer.
- Add file/folder ingestion and source path extraction.
- Add filename/folder game assignment heuristics.
- Add timestamp derivation and gaming-session grouping.
- Add selection plus bulk tag/collection metadata operations.
- Test the pure queue logic without Bunny upload side effects.

Third issue: Build the bulk queue UI shell.

- Replace `/upload` with the queue surface for one or many rows.
- Add normal file picker, folder picker where supported, and drop zone.
- Render session groups, row editing, selected state, and status placeholders.
- Add bulk action bar for tags and collection metadata.
- Add unsaved navigation warnings.
- Add responsive desktop table and narrow row-card layouts.

Fourth issue: Wire real bulk upload execution.

- Extract shared upload helpers from the current single-file upload hook.
- Implement the fixed 3-concurrent queue runner.
- Hash, create, and TUS-upload selected ready rows.
- Add row/global pause and resume for active uploads.
- Add cancel, retry, duplicate, and upload error states.
- Keep post-upload filing minimal until the filing slice.

Fifth issue: Add post-upload filing.

- Apply queued tags after each successful clip upload.
- Add uploaded clips to the optional user-selected collection.
- Call the session collection endpoint once per completed session batch.
- Add `filing_error` retry that uses the existing uploaded clip identity without re-uploading.
- Collapse fully saved sessions into summaries with links to their created collections.

Sixth issue: Add library and game-page upload entry points.

- Add the library drag/drop overlay.
- Route dropped files and folders into `/upload`.
- Add game-page upload action with fallback category context.
- Add folder drag/drop traversal where supported.
- Manually verify library, game page, and upload route handoffs.

After that, build the queue using existing upload APIs plus the new session endpoint. Do not add backend batch clip-prepare endpoints until the frontend path exposes a real bottleneck.
