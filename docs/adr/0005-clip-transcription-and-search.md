# ADR-0005: Clip transcription, summaries and natural-language search

**Status:** Proposed (2026-09-28)

## Context

The library can be browsed by game, playlist, tag and date, but not by what happens in a clip. Users want to ask for clips the way they remember them: "clips from 2025 where I was playing Call of Duty and something funny happened". Most of the signal for that is in the clip's audio (voice chat, callouts, reactions), which Reelshelf never looks at.

Legend detection (`api/ApexLegends/LegendDetection/`) already runs a paid model over clips after Bunny finishes encoding. It queues runs in Postgres, has background workers claim them with SKIP LOCKED, keeps every attempt for comparison, and has an admin review page. A pipeline for all clips can follow the same shape.

Things we found that shaped the decision:

- `gpt-transcribe` costs $0.0045 per audio minute and accepts up to 25 MB per request. It takes `prompt`, `keywords` and `languages` hints. It returns no segment or word timestamps and no speaker labels.
- Transcription models drop non-speech audio. A clip where everyone just laughs, or one with game audio and no talking, transcribes to little or nothing. On its own, a transcript can't answer "something funny happened".
- Most of an example query is structured: "2025" and "Call of Duty" are filters on the clip's date and game category. Only "something funny" needs semantic matching.

## Decision

- **Three stages, each with its own run table.** Transcription (`clip_transcription_run`) produces text from audio. Summarisation (`clip_summary_run`) turns the transcript and clip metadata into a description, tags, quotes, names and an embedding. Search reads the latest succeeded summary. Keeping the stages apart lets us rerun summaries with a new prompt or model without paying to transcribe again, and lets us re-embed without doing either. As in `legend_detection_run`, runs are kept, not overwritten. A clip's current transcript or summary is its latest succeeded run.
- **Only clips owned by whitelisted accounts are processed.** The whitelist already marks the unlimited storage tier (ADR-0002). Transcription is a paid call per clip and records other people's voices, so it is limited to accounts we trust and pay for. The check runs when a clip is queued (encoding webhook or backfill). A clip already processed keeps its transcript if the owner later leaves the whitelist.
- **Transcripts are for search only.** Owners never see transcripts or summaries. Admins see them on a review page like `/legend-detection`. Showing them to owners, or letting owners edit them, is a later decision.
- **Transcription sends the clip's audio as one request.** ffmpeg reads the Bunny HLS stream (as `FFmpegService` already does) and writes mono, low-bitrate audio only. At 32 kbps, 25 MB holds about 100 minutes, so clips aren't chunked. A clip whose audio is still too large is marked failed with a clear error. The request's `prompt` and `keywords` name the game and, for Apex clips, the legends on the reference sheet.
- **Results are whole clips, not moments.** Because search only needs to find clips, `gpt-transcribe` works without timestamps. Moment-level results would need whisper-1, the diarize model or our own chunk offsets. We'll revisit that if we want it.
- **The summariser reuses the legend detection provider setup.** It builds its own options section (`ClipSummary:Provider`, `Model`, `ReasoningEffort`, `Providers:<provider>:ApiKey`) and uses a factory like `LegendRecognizerFactory`, so we can swap providers and models and compare them. Its input is the transcript, game name, clip title, clip date and, for Apex clips, the detected legend. It also says when the transcript is empty or short, so a clip with no speech still gets a description. Its structured output is:
  - `description`: 2–3 sentences on what happens, written to be searched.
  - `mood_tags`: chosen from a fixed list in the prompt (initially `funny`, `clutch`, `fail`, `rage`, `hype`, `wholesome`, `chaotic`, `chill`).
  - `quotes`: a few short notable lines taken word for word from the transcript.
  - `people`: names or nicknames spoken in the clip.
- **Embeddings are `text-embedding-3-small` in pgvector.** The summary run stores a 1536-dimension `vector` embedding of the description (plus tags) and the embedding model name. A migration enables the `vector` extension, so the Postgres image moves to one that includes pgvector. At the expected volume (hundreds of clips per user) an exact scan is fast enough, so no ANN index is added until it's needed.
- **Search pairs structured filters with semantic ranking.** A model turns the user's query into structured output: a date range, game categories matched against the user's own categories, mood tags, people, and a leftover semantic phrase. SQL applies the filters to the signed-in user's own clips, then orders by cosine distance between the phrase's embedding and each summary's embedding. A query with no semantic phrase orders by date. Clips without a summary can still match a pure filter query, but never a semantic one. Search only ever covers the searcher's own clips, never clips shared with them or other people's clips they can view.
- **Each stage has its own on/off switch.** Dev and prod share the database and job queues, so `ClipTranscription:AutoQueue` and `ClipSummary:AutoQueue` decide whether an instance queues work. Each has its own `Concurrency`. A succeeded transcription queues the summary run, and there is a backfill for existing clips at each stage.

## Consequences

- Clips from whitelisted accounts, and the voices in them, go to OpenAI (and to whichever provider runs summaries). OpenAI doesn't train on API data by default. If signup-wide transcription is ever considered, it needs per-user consent.
- Cost is small at the expected volume: a few hundred short clips cost about a dollar or two to transcribe. Summaries and embeddings add a few cents. Usage totals are recorded per run, as for legend detection.
- The Postgres image in `PlusCosmic/infrastructure`, and any Postgres used by tests, must include pgvector before the stage 2 migration runs. `CREATE EXTENSION vector` may need a superuser the first time.
- Search quality depends on the summariser. The fixed mood-tag list and the prompt are versioned (a hash on each run, like `prompt_version`), so changes can be compared on the admin page.
- The three stages depend on each other only through stored rows. Any stage can be rebuilt, rerun or replaced without touching the others.

## Amendment (2026-09-29): transcription through OpenRouter with Gemini

The first backfill with `gpt-transcribe` recorded 408 of 714 clips as having no speech, and many of them do. The audio was fine: full length, one stereo track, the same levels as clips that transcribed. The model returned an empty transcript, and whether it did varied from one call to the next on the same clip. On a sample of 20 empty clips, 5 controls and 4 clips the owner confirmed have speech:

- `gpt-transcribe` recovered 5 of 20, or 7 with `chunking_strategy=auto`.
- `gpt-4o-transcribe` with chunking recovered nearly all of them, but mixed in invented Norwegian, Russian, Chinese and Portuguese words and dropped lines.
- `whisper-1` produced text for every clip, including repeating its own prompt over silent ones.
- `gemini-3.5-transcribe` with the game's vocabulary was the most accurate, found speech in every clip that has it, and left silent clips empty. Without the vocabulary it returned nothing for one clip every time.
- `deepgram/nova-3` also found every clip with speech, was fastest, and mishears more.

Calling Gemini directly allows 100 requests a day, so transcription goes through OpenRouter's `audio/transcriptions` endpoint, where the model id picks the provider and our own OpenAI and Gemini keys are used first. The default is `google/gemini-3.5-transcribe` with the vocabulary, falling back to `deepgram/nova-3` when it hears nothing, before a clip is recorded as having no speech. Transcripts include in-game voice lines and announcers as well as players; the summary stage (#134) has to tell them apart.
