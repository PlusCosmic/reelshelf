# Architecture Deepening Opportunities

Captured on 2026-05-10 from an `improve-codebase-architecture` review. These are candidates for future exploration, not accepted design decisions.

## 1. Gaming Session Playlist Module

**Files**

- `api/Playlists/PlaylistService.cs`

**Problem**

Gaming session creation crosses playlist creation, collaborator management, clip search, date-window rules, ordering, and batch insertion. It also calls `GetClipsForCategory` with `int.MaxValue`, which is clamped internally, so the interface hides a likely behavioral mismatch.

**Solution**

Pull gaming session playlist creation into its own deeper module with an explicit session clip selection interface.

**Benefits**

This improves locality for session semantics, clarifies tests for participant inclusion and time-window behavior, and reduces accidental coupling to paged clip browsing.

## 2. Apex Detection Module

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
