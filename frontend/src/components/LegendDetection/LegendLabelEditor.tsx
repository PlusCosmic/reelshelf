import { useState } from "react";
import { Badge, Button, Select, type SelectOption } from "@/components/ui";
import {
  useDeleteLegendLabel,
  useLegendNames,
  useSetLegendLabel,
} from "@/hooks/queries";
import type {
  LegendDetectionReviewClip,
  LegendDetectionRun,
} from "@/shared/services/legendDetection";
import { labelFromRun, labelVerdict } from "./legendReview";

const none = "__none";

/**
 * The legends actually in the clip, which every run is scored against. Starts from the saved label, or the
 * shown run's answer when there isn't one, so confirming a correct result is one click.
 */
export function LegendLabelEditor({
  clip,
  run,
}: {
  clip: LegendDetectionReviewClip;
  run: LegendDetectionRun | undefined;
}) {
  const legends = useLegendNames();
  const setLabel = useSetLegendLabel();
  const clearLabel = useDeleteLegendLabel();
  const succeededRun = run?.status === "succeeded" ? run : undefined;
  const initial = clip.label ??
    (succeededRun ? labelFromRun(succeededRun) : null) ?? {
      playerLegend: null,
      teammateLegends: [],
    };
  const [player, setPlayer] = useState(initial.playerLegend ?? none);
  const [teammateOne, setTeammateOne] = useState(
    initial.teammateLegends[0] ?? none,
  );
  const [teammateTwo, setTeammateTwo] = useState(
    initial.teammateLegends[1] ?? none,
  );

  const legendOptions: SelectOption[] = (legends.data ?? []).map((legend) => ({
    value: legend,
    label: legend,
  }));
  const playerOptions = [
    { value: none, label: "Can't tell" },
    ...legendOptions,
  ];
  const teammateOptions = [{ value: none, label: "None" }, ...legendOptions];
  const verdict = labelVerdict(succeededRun ?? null, clip.label);
  const pending = setLabel.isPending || clearLabel.isPending;

  function save(playerLegend: string | null, teammateLegends: string[]) {
    setLabel.mutate({ clipId: clip.clipId, playerLegend, teammateLegends });
  }

  return (
    <div className="rs-legend-label">
      <div className="rs-legend-result-heading">
        <h3 className="rs-eyebrow">Actual legends</h3>
        {verdict === "correct" ? (
          <Badge tone="accent">Result matches</Badge>
        ) : verdict === "wrong" ? (
          <Badge tone="danger">Result is wrong</Badge>
        ) : clip.label ? null : (
          <Badge>Not labelled</Badge>
        )}
      </div>
      <div className="rs-legend-label-fields">
        <label>
          <span className="rs-meta">You</span>
          <Select
            aria-label="Your legend"
            options={playerOptions}
            value={player}
            onValueChange={setPlayer}
          />
        </label>
        <label>
          <span className="rs-meta">Teammate</span>
          <Select
            aria-label="First teammate's legend"
            options={teammateOptions}
            value={teammateOne}
            onValueChange={setTeammateOne}
          />
        </label>
        <label>
          <span className="rs-meta">Teammate</span>
          <Select
            aria-label="Second teammate's legend"
            options={teammateOptions}
            value={teammateTwo}
            onValueChange={setTeammateTwo}
          />
        </label>
      </div>
      <div className="rs-legend-label-actions">
        {succeededRun ? (
          <Button
            size="sm"
            variant="primary"
            disabled={pending}
            onClick={() => {
              const detected = labelFromRun(succeededRun);
              save(detected.playerLegend, detected.teammateLegends);
            }}
            title="Save the shown result as the label (c)"
          >
            Mark correct
          </Button>
        ) : null}
        <Button
          size="sm"
          disabled={pending || legends.isLoading}
          onClick={() =>
            save(
              player === none ? null : player,
              [teammateOne, teammateTwo].filter((legend) => legend !== none),
            )
          }
        >
          Save label
        </Button>
        {clip.label ? (
          <Button
            size="sm"
            variant="ghost"
            disabled={pending}
            onClick={() => clearLabel.mutate(clip.clipId)}
          >
            Clear
          </Button>
        ) : null}
        {setLabel.isError ? (
          <span className="rs-legend-error">{setLabel.error.message}</span>
        ) : null}
      </div>
    </div>
  );
}
