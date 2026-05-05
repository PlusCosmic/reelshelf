# Clips Frontend Backend Gaps

The new Reelshelf UI is wired to the existing category, clip, tag, playlist, and clip request APIs where they already support the design.

Mocked or presentational-only pieces:

- Timestamped clip notes and comment threads. The current clip API exposes Bunny moments but not user-authored comments, authors, or note CRUD.
- Watch party state. Synced playback rooms, online friends, shared chat, and invite state need a collaboration backend and probably realtime transport.
- Collection cover composition on the collection index. Playlist summaries expose counts but not representative clip thumbnails, so the frontend derives covers from locally loaded clips.
- Upload drag-and-drop and progress lanes. The existing frontend can create video requests, but the design includes richer file transfer state that is larger than a small backend addition.
- Persisted design preferences for view density/theme. The settings surface is presentational until the desired preference schema is confirmed.
