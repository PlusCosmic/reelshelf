import { commonCopy } from "./common";

/** The transcription and summary review page (admin only). */
export const clipTranscriptionCopy = {
  adminsOnly: "Transcription review is only available to admins.",
  confirmBackfill:
    "Queue a transcription run for every encoded clip of a whitelisted owner that has never had one? Each run is a paid model call.",
  confirmRetryEmpty:
    "Transcribe again, with the configured model, every clip that came back with no speech from another model? Each run is a paid model call.",
  confirmBackfillSummaries:
    "Queue a summary for every transcribed clip that has never had one, with the configured model? Each run is a paid model call.",
  eyebrow: "Transcription",
  title: "Check what the model heard.",
  backfillSummaries: "Backfill summaries",
  retryNoSpeech: "Retry no-speech clips",
  retrying: (clips: number) => `Retrying ${commonCopy.clips(clips)}.`,
  summarising: (clips: number) => `Summarising ${commonCopy.clips(clips)}.`,
  noClips: "No whitelisted owner has any clips yet.",
  outcomes: {
    transcribed: "Transcribed",
    silent: "No speech",
    noAudio: "No audio",
    failed: "Failed",
    queued: "Queued",
    notRun: "Not run",
  },

  usage: {
    noSpeechTitle: "Clips with audio but no words",
    noSpeech: "No speech",
    noAudioTitle: "Clips without an audio track; never sent",
    noAudio: "No audio",
    fallbackTitle:
      "Runs where the model heard nothing and the fallback model was tried; in brackets, how many it found speech in",
    fallback: "Fallback",
    audio: "Audio",
    estCostTitle:
      "From the model's price in ClipTranscription:CostPerMinuteUsd",
    estCost: "Est. cost",
    audioMinutes: (minutes: number) => `${minutes.toFixed(1)} min`,
    summaryModel: "Summary model",
    embeddingTokens: "Embedding tokens",
  },

  detail: {
    chars: (count: number) => `${count} chars`,
    heardNothing: (fallbackModel: string) =>
      ` (heard nothing; transcript from ${fallbackModel})`,
    heardNothingEither: (fallbackModel: string) =>
      ` (${fallbackModel} heard nothing either)`,
    audioSeconds: (seconds: number) => ` · ${Math.round(seconds)}s of audio`,
    silent: "The clip has audio, but no words came back.",
    noAudio: "The clip has no audio track, so nothing was sent.",
    promptSummary: (version: string | null, keywords: number) =>
      `Prompt ${version ? `(${version})` : ""} and ${keywords} keywords`,
    keywords: (keywords: string[]) => `\n\nKeywords: ${keywords.join(", ")}`,
  },

  summary: {
    heading: "Summary",
    notLoaded: "Summaries could not be loaded.",
    notSummarised: "Not summarised yet.",
    promptVersion: (version: string) => ` · prompt ${version}`,
    noMoodTags: "No mood tags.",
    people: (
      people: string[],
      hasEmbedding: boolean,
      embeddingModel: string | null,
    ) =>
      `People: ${people.length > 0 ? people.join(", ") : "none mentioned"} · ${hasEmbedding ? `embedded with ${embeddingModel}` : "no embedding"}`,
    providerPlaceholder: "Provider (default if empty)",
    providerLabel: "Summary provider",
    modelLabel: "Summary model",
    summariseAgain: "Summarise again",
  },
} as const;
