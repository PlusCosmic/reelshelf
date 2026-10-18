# Video pipeline: working notes

Short-lived notes for replacing Bunny Stream with our own storage, transcoding and delivery. They hold what
we know and what we've decided while the design is still moving, so separate sessions (code and chat) work
from the same facts. When the design settles, the decisions move into an ADR under `docs/adr/` and this folder
is deleted.

| File                                                         | What it holds                                                             |
| ------------------------------------------------------------ | ------------------------------------------------------------------------- |
| [current-state.md](current-state.md)                         | What the app relies on from the video host today, after the #188 refactor |
| [playback-and-codecs.md](playback-and-codecs.md)             | HLS vs progressive MP4, AV1, the H.264 fallback and downloads             |
| [storage-transcoding-queue.md](storage-transcoding-queue.md) | R2, encoding and the queue: decisions, costs, and how they fit the app    |
| [workers-and-jobs.md](workers-and-jobs.md)                   | The worker service, job types, thumbnails, downloads and trimming         |
| [r2-bucket.md](r2-bucket.md)                                 | The R2 bucket: config, object layout, lifecycle, CORS, caching, Activity  |
| [bunny-migration.md](bunny-migration.md)                     | Moving the Bunny clips to R2, then leaving Bunny                          |
| [open-questions.md](open-questions.md)                       | Undecided points and known follow-ups                                     |

Conventions:

- Write down a decision in the file it belongs to, with the date and a line on why. Keep superseded options in
  a short "Considered" list rather than deleting them.
- Name code by path (`api/Video/IClipVideoHost.cs`) so notes can be checked against the repo.
- These notes describe the plan; `AGENTS.md` and the code describe what is actually built.
