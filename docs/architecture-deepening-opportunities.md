# Architecture Deepening Opportunities

Captured on 2026-05-10 from an `improve-codebase-architecture` review. These are candidates for future exploration, not accepted design decisions.

## 1. Clip Library Module

**Files**

- `api/Core/ClipService.cs`
- `frontend/src/components/Reelshelf/useLibraryData.ts`
- `frontend/src/components/Reelshelf/reelshelf-model.ts`

**Problem**

The library view fetches categories, issues one clip query per category, flattens clips, then rebuilds shelves client-side. The interface leaks paging, category iteration, preview sizing, and shelf aggregation to callers.

**Solution**

Create a deeper Clip Library module behind one interface that returns the shelf-ready library shape for the current user.

**Benefits**

This improves locality for library home behavior, reduces client orchestration, and gives tests one domain result to assert instead of many query states.

## 2. Playlist Collaboration Module

**Files**

- `api/Playlists/PlaylistService.cs`

**Problem**

Resolving the Discord user, checking playlist collaboration, then proceeding is repeated across nearly every playlist operation. The playlist interface makes each operation carry collaborator authorization knowledge.

**Solution**

Create a deeper Playlist Access module that resolves the acting user and playlist membership once, then exposes playlist operations through that context.

**Benefits**

This improves locality for collaboration rules, reduces repeated error modes, and lets tests cover playlist access once instead of restating it per operation.

## 3. Gaming Session Playlist Module

**Files**

- `api/Playlists/PlaylistService.cs`

**Problem**

Gaming session creation crosses playlist creation, collaborator management, clip search, date-window rules, ordering, and batch insertion. It also calls `GetClipsForCategory` with `int.MaxValue`, which is clamped internally, so the interface hides a likely behavioral mismatch.

**Solution**

Pull gaming session playlist creation into its own deeper module with an explicit session clip selection interface.

**Benefits**

This improves locality for session semantics, clarifies tests for participant inclusion and time-window behavior, and reduces accidental coupling to paged clip browsing.

## 4. Apex Detection Module

**Files**

- `api/ApexLegends/LegendDetection/DetectionEndpoints.cs`
- `api/ApexLegends/LegendDetection/DetectionQueueService.cs`
- `api/ApexLegends/LegendDetection/DetectionBackgroundService.cs`

**Problem**

Detection policy is split across endpoint code, queue adapter, Redis result polling, magic `ApexLegend.None` values, and hard-coded Bunny thumbnail URLs. The endpoint owns too much implementation.

**Solution**

Create a deeper Apex Detection workflow module that owns eligibility, screenshot URL generation, enqueueing, retry and reprocess policy, and result application.

**Benefits**

This improves locality for detection rules, creates a smaller endpoint interface, and lets tests exercise the workflow without Redis or HTTP endpoint wiring.
