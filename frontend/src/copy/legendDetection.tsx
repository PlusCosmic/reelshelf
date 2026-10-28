import type { ReactNode } from "react";
import { commonCopy } from "./common";
import { plural } from "./plural";

/** The legend detection review tools (admin only): the review page, the baseline and the evals. */
export const legendDetectionCopy = {
  /** An answer of no legend: the owner's own legend can't be seen. */
  cantTell: "Can't tell",
  noApexClips: "There are no Apex Legends clips.",
  backToReview: "Back to review",

  review: {
    adminsOnly: "Legend detection review is only available to admins.",
    confirmBackfill: (effort: string) =>
      `Queue a detection run at ${effort} for every Apex Legends clip that has never had one? Each run is a paid model call.`,
    defaultReasoning: "the default reasoning",
    confirmArchive:
      "Archive every detection run, including any still queued? Clips will show as not run and the next backfill queues them all again. Usage totals are kept.",
    eyebrow: "Legend detection",
    title: "Check what the model saw.",
    baseline: "Baseline",
    evals: "Evals",
    archiving: "Archiving…",
    startFresh: "Start fresh",
    archived: (runs: number) => `Archived ${plural(runs, "run", "runs")}.`,
    filters: {
      needsReview: "Needs review",
      confident: "Confident",
      active: "In progress",
      unlabelled: "Unlabelled",
      wrong: "Labelled wrong",
    },
    hint: "j / k to move · c marks correct",
    notRun: "Not run",
    noLegend: "No legend",
    matchesLabel: "Matches label",
    doesntMatchLabel: "Doesn't match label",
  },

  usage: {
    batch: "Batch",
    labelledTitle: "Succeeded runs on labelled clips",
    labelled: "Labelled",
    youTitle: "Your legend correct",
    you: "You",
    squadTitle:
      "Every teammate legend correct, for prompts that ask for teammates",
    squad: "Squad",
    confidentMistakesTitle: "Wrong legends reported at 80%+ confidence",
    confidentMistakes: "Confident mistakes",
    archived: (date: string) => `Archived ${date}`,
    current: "Current",
  },

  detail: {
    framesHeading: "Frames sent to the model",
    screenshot: (index: number) => `Screenshot ${index}`,
    detected: "Detected",
    hudNotFound: "HUD not found",
    you: "You",
    teammate: (slot: number) => `Teammate ${slot}`,
    legend: "Legend",
    name: "Name",
    cachedTokens: (tokens: string) => ` (${tokens} cached)`,
    time: "Time",
    framesAttempts: "Frames · attempts",
    notIdentified: "Not identified",
  },

  label: {
    none: "None",
    heading: "Actual legends",
    resultMatches: "Result matches",
    resultWrong: "Result is wrong",
    notLabelled: "Not labelled",
    you: "You",
    yourLegend: "Your legend",
    teammate: "Teammate",
    firstTeammate: "First teammate's legend",
    secondTeammate: "Second teammate's legend",
    markCorrectTitle: "Save the shown result as the label (c)",
    markCorrect: "Mark correct",
    saveLabel: "Save label",
    clear: "Clear",
  },

  baseline: {
    adminsOnly: "The legend baseline is only available to admins.",
    noResultGroup: "No result yet",
    eyebrow: "Legend detection · Baseline",
    title: "Confirm every clip's legend.",
    intro:
      "Each clip starts from its label, or the current result until it has one. Scan a group, fix the odd one out, confirm the rest.",
    progress: (confirmed: ReactNode, total: number) => (
      <>
        {confirmed} of {total} confirmed
      </>
    ),
    progressLabel: "Clips confirmed",
    showConfirmed: "Show confirmed",
    undo: (
      <>
        Undo <kbd>u</kbd>
      </>
    ),
    savingFailed: (error: string) => `Saving failed: ${error}`,
    keys: (
      <>
        <kbd>j</kbd> <kbd>k</kbd> clip · <kbd>↑</kbd> <kbd>↓</kbd> group ·{" "}
        <kbd>Enter</kbd> change · <kbd>x</kbd> can't tell · <kbd>c</kbd> confirm
        group · <kbd>1</kbd>–<kbd>6</kbd> frame
      </>
    ),
    allConfirmed:
      "Every clip is confirmed. Tick “Show confirmed” to check them again.",
    groupSummary: (group: {
      total: number;
      done: boolean;
      pending: number;
      lowestConfidence: string | null;
      moved: number;
    }) =>
      `${commonCopy.clips(group.total)} · ${group.done ? "All confirmed" : `${group.pending} to confirm`}${group.lowestConfidence !== null ? ` · lowest ${group.lowestConfidence}` : ""}${group.moved > 0 ? ` · ${group.moved} relabelled` : ""}`,
    confirm: (count: number, asCantTell: boolean) =>
      `Confirm ${count}${asCantTell ? " as can't tell" : ""}`,
    footnote: (threshold: string, cantTell: string) =>
      `Results below ${threshold} are marked. ${cantTell} means the owner's own legend can't be seen in any frame.`,
    tile: {
      label: (title: string, answer: string) => `${title}: ${answer}`,
      noResult: "no result",
    },
    picker: {
      hint: "Type a few letters · Enter picks the first · Esc cancels",
      placeholder: "val, mag, hor…",
      label: "Legend",
    },
    panel: {
      label: "Selected clip",
      labelled: (date: string) => `Labelled ${date}`,
      currentResult: (
        model: string,
        confidence: string | null,
        hudMissed: boolean,
      ) =>
        `Current result · ${model}${confidence !== null ? ` · ${confidence}` : ""}${hudMissed ? " · HUD not seen" : ""}`,
      noResult: "No result yet: pick a legend",
      openInReview: "Open in review",
      frame: (index: number) => `Frame ${index}`,
      answer: "Answer",
      changeLegend: (
        <>
          Change legend <kbd>Enter</kbd>
        </>
      ),
      cantTell: (
        <>
          Can't tell <kbd>x</kbd>
        </>
      ),
      note: "A change saves at once and moves to the next clip. Confirming a group saves the rest of it as shown.",
    },
  },

  evals: {
    adminsOnly: "Legend evals are only available to admins.",
    eyebrow: "Legend detection · Evals",
    title: "Which model is worth it?",
    intro:
      "Each eval runs one model on every confirmed clip and is scored against the labels as they are now, so fixing a label re-scores every eval. Eval runs never change a clip's legend.",
    baselineLink: (clips: number) => `Baseline: ${clips} clips confirmed`,
    loading: "Loading evals…",
    notLoaded: "Evals could not be loaded.",
    empty:
      "No evals yet. Confirm clips on the baseline, then run a model on them.",
    chartHeading: "Accuracy against cost",

    form: {
      pickModel: "Pick a provider and model to see an estimate.",
      noEstimate: "No finished eval of this model to estimate from.",
      estimate: (cost: string | null, wall: string | null) =>
        `About ${cost ?? "an unknown cost"}${wall === null ? "" : ` and ${wall}`}, from its last eval.`,
      confirm: (model: string | null, clips: number) =>
        `Run ${model ?? "the default model"} on all ${clips} baseline clips? Each run is a paid model call.`,
      starting: "Starting…",
      run: (clips: number) => `Run on baseline (${clips} clips)`,
    },

    table: {
      scaleTitle: (from: number) => `Bars run from ${from}% to 100%`,
      accuracy: "Accuracy (95% range)",
      answered: "Answered",
      confidentWrongTitle: "Wrong legends given at 80%+ confidence",
      confidentWrong: "Confident wrong",
      cost: "Cost",
      latencyTitle: "The model call alone, for succeeded runs",
      latency: "Latency p50 / p95",
      wallTime: "Wall time",
      progress: (done: number, total: number) => ` · running ${done}/${total}`,
      failed: (count: number) => ` · ${count} failed`,
      setPrice: "Set price",
      billed: "billed",
      running: "running",
      deleteLabel: (name: string) => `Delete ${name} eval`,
      deleteTitle: "Delete eval",
      confirmDelete: (name: string) =>
        `Delete the ${name} eval and its runs? Queued runs are cancelled.`,
    },

    chart: {
      empty: "Costed evals appear here once they have results and a price.",
      label: "Accuracy against cost per 1,000 clips for each eval",
      point: (name: string, accuracy: string, cost: string, p50: string) =>
        `${name}: ${accuracy}% at ${cost} per 1k clips, p50 ${p50}`,
      caption:
        "Accuracy against cost per 1,000 clips, log scale. Up and to the left is better.",
    },

    prices: {
      heading: "Prices, $ per million tokens",
      intro:
        "Used for evals the provider doesn't bill per run. Cached and cache-write prices fall back to input when empty.",
      input: "Input",
      cacheWrite: "Cache write",
      output: "Output",
      fields: {
        input: "input",
        cachedInput: "cached input",
        cacheWrite: "cache write",
        output: "output",
      },
      fieldLabel: (model: string, field: string) => `${model} ${field} price`,
    },

    misses: {
      heading: (name: string) => `Wrong answers · ${name}`,
      count: (wrong: number, scored: number) => `${wrong} of ${scored}`,
      empty: "No wrong answers in this eval.",
      failed: (error: string | null) =>
        `Failed: ${error ?? "no error recorded"}`,
      said: (answer: ReactNode) => <>Said {answer}</>,
      baseline: (answer: ReactNode) => <>Baseline {answer}</>,
      modelIsRight: "Model is right",
      mixUps: "Most common mix-ups",
    },

    cascade: {
      heading: "Cascade",
      intro:
        "Run the first model on every clip and send the answers it is unsure of to the second, as production's escalation does. Worked out from both evals' answers on the same clips, so nothing is run again.",
      option: (name: string, provider: string) => `${name} (${provider})`,
      first: "First",
      firstLabel: "First model",
      escalateTo: "Escalate to",
      escalationLabel: "Escalation model",
      threshold: (picked: string, production: string) =>
        `Escalate below ${picked} (production: ${production})`,
      pickTwo: "Pick two different evals.",
      noCommonClips: "These evals have no finished clips in common.",
      escalateBelow: "Escalate below",
      escalated: "Escalated",
      accuracy: "Accuracy",
      wrong: "Wrong",
      meanTime: "Mean time",
      escalatedOf: (escalated: number, clips: number) =>
        `${escalated} of ${clips}`,
      setPrices: "Set prices",
      alone: (accuracy: string, cost: string | null) =>
        `${accuracy} at ${cost ?? "unknown cost"}`,
      aloneSummary: (
        first: string,
        firstAlone: string,
        escalation: string,
        escalationAlone: string,
      ) =>
        `Alone, ${first} is ${firstAlone} and ${escalation} is ${escalationAlone} per 1k clips.`,
    },
  },
} as const;
