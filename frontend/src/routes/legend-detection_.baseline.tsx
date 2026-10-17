import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Button, Checkbox } from "@/components/ui";
import {
  answerLabel,
  BaselinePanel,
  BaselineTileButton,
  LegendPortrait,
} from "@/components/LegendDetection/Baseline";
import {
  buildGroups,
  cantTellKey,
  confirmLabels,
  groupKeyOf,
  lowConfidenceBelow,
  noResultKey,
  type BaselineGroup,
} from "@/components/LegendDetection/legendBaseline";
import { formatConfidence } from "@/components/LegendDetection/legendReview";
import {
  useChangeBaselineLabels,
  useCurrentUser,
  useLegendBaseline,
  useLegendNames,
  type BaselineLabelChange,
} from "@/hooks/queries";

export const Route = createFileRoute("/legend-detection_/baseline")({
  component: LegendBaselineRoute,
});

function LegendBaselineRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        The legend baseline is only available to admins.
      </div>
    );
  return <LegendBaseline />;
}

/** One step the page can take back: the change that reverses it, and where moved clips sat before it. */
type UndoStep = {
  inverse: BaselineLabelChange;
  origins: Map<string, string>;
};

function groupTitle(group: BaselineGroup) {
  if (group.key === noResultKey) return "No result yet";
  if (group.key === cantTellKey) return "Can't tell";
  return group.key;
}

function LegendBaseline() {
  const baseline = useLegendBaseline();
  const names = useLegendNames();
  const legends = useMemo(() => names.data ?? [], [names.data]);
  const change = useChangeBaselineLabels();
  const mutateLabels = change.mutate;
  const clips = useMemo(() => baseline.data ?? [], [baseline.data]);

  const [showConfirmed, setShowConfirmed] = useState(false);
  // Clips relabelled during this visit stay in the group they were found in, keyed by clip id.
  const [origins, setOrigins] = useState<Map<string, string>>(new Map());
  const [undo, setUndo] = useState<UndoStep[]>([]);
  const [selectedId, setSelectedId] = useState<string>();
  const [frame, setFrame] = useState(0);
  const [picking, setPicking] = useState(false);

  const groups = useMemo(
    () => buildGroups(clips, origins, showConfirmed),
    [clips, origins, showConfirmed],
  );
  const tiles = useMemo(() => groups.flatMap((group) => group.tiles), [groups]);
  const selected =
    tiles.find((tile) => tile.clip.clipId === selectedId) ?? tiles[0];
  const selectedGroup = groups.find((group) =>
    group.tiles.some((tile) => tile === selected),
  );
  const confirmed = clips.filter((clip) => clip.label !== null).length;

  function select(clipId: string | undefined) {
    setSelectedId(clipId);
    setFrame(0);
    setPicking(false);
  }

  function apply(
    set: BaselineLabelChange,
    inverse: BaselineLabelChange,
    nextOrigins = origins,
  ) {
    mutateLabels(set);
    setUndo((steps) => [...steps.slice(-49), { inverse, origins }]);
    setOrigins(nextOrigins);
  }

  /** Labels the selected clip and moves on to the next one in the list. */
  function relabel(legend: string | null) {
    if (!selected) return;
    const { clip } = selected;
    const group =
      selectedGroup?.key ?? groupKeyOf(selected.answer, selected.source);
    const nextOrigins = new Map(origins);
    if (!nextOrigins.has(clip.clipId)) nextOrigins.set(clip.clipId, group);
    const index = tiles.indexOf(selected);
    apply(
      { set: [{ clipId: clip.clipId, playerLegend: legend }], clear: [] },
      clip.label
        ? {
            set: [
              { clipId: clip.clipId, playerLegend: clip.label.playerLegend },
            ],
            clear: [],
          }
        : { set: [], clear: [clip.clipId] },
      nextOrigins,
    );
    select(tiles[index + 1]?.clip.clipId ?? clip.clipId);
  }

  /** Saves every unconfirmed clip in the group as shown, then jumps to the next group with work left. */
  function confirmGroup(group: BaselineGroup | undefined) {
    if (!group || group.pending.length === 0) return;
    const labels = confirmLabels(group);
    apply(
      { set: labels, clear: [] },
      { set: [], clear: labels.map((label) => label.clipId) },
    );
    const next = groups.find(
      (other) => other !== group && other.pending.length > 0,
    );
    select(next?.tiles[0]?.clip.clipId);
  }

  function undoLast() {
    const step = undo[undo.length - 1];
    if (!step) return;
    mutateLabels(step.inverse);
    setOrigins(step.origins);
    setUndo((steps) => steps.slice(0, -1));
  }

  function moveGroup(delta: number) {
    const index = groups.findIndex((group) => group === selectedGroup);
    const next =
      groups[Math.max(0, Math.min(groups.length - 1, index + delta))];
    if (next?.tiles[0]) select(next.tiles[0].clip.clipId);
  }

  // The keys mirror the review page's j / k, with Enter to pick a legend, x for can't tell, c to confirm the
  // selected clip's group and u to undo. They are ignored while typing, so the picker keeps its own keys.
  const keys = useRef<(event: KeyboardEvent) => void>(() => {});
  keys.current = (event: KeyboardEvent) => {
    if (event.metaKey || event.ctrlKey || event.altKey) return;
    if (
      event.target instanceof HTMLElement &&
      event.target.closest("input, textarea, select, [contenteditable]")
    )
      return;
    const index = selected ? tiles.indexOf(selected) : -1;
    const handled = () => event.preventDefault();
    switch (event.key) {
      case "j":
      case "ArrowRight":
        handled();
        select(tiles[Math.min(tiles.length - 1, index + 1)]?.clip.clipId);
        break;
      case "k":
      case "ArrowLeft":
        handled();
        select(tiles[Math.max(0, index - 1)]?.clip.clipId);
        break;
      case "ArrowDown":
        handled();
        moveGroup(1);
        break;
      case "ArrowUp":
        handled();
        moveGroup(-1);
        break;
      case "Enter":
      case "/":
        // Enter on a clip's tile picks its legend; on any other button it presses the button.
        if (
          !selected ||
          (event.target instanceof HTMLButtonElement &&
            !event.target.dataset.baselineClip)
        )
          return;
        handled();
        setPicking(true);
        break;
      case "x":
        handled();
        relabel(null);
        break;
      case "c":
        handled();
        confirmGroup(selectedGroup);
        break;
      case "u":
        handled();
        undoLast();
        break;
      case "Escape":
        setPicking(false);
        break;
      default:
        if (/^[1-6]$/.test(event.key) && selected) {
          const next = Number(event.key) - 1;
          if (next < selected.clip.frameUrls.length) setFrame(next);
        }
    }
  };
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => keys.current(event);
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const scrollId = selected?.clip.clipId;
  useEffect(() => {
    if (!scrollId) return;
    document
      .querySelector(`[data-baseline-clip="${scrollId}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [scrollId]);

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">Legend detection · Baseline</div>
          <h1 className="rs-display rs-h2">Confirm every clip's legend.</h1>
          <p className="rs-meta rs-baseline-intro">
            Each clip starts from its label, or the current result until it has
            one. Scan a group, fix the odd one out, confirm the rest.
          </p>
        </div>
        <div className="rs-baseline-progress">
          <div className="rs-baseline-progress-text">
            <strong>{confirmed}</strong> of {clips.length} confirmed
          </div>
          <div
            className="rs-baseline-progress-track"
            role="progressbar"
            aria-label="Clips confirmed"
            aria-valuemin={0}
            aria-valuemax={clips.length}
            aria-valuenow={confirmed}
          >
            <span
              style={{
                width: `${clips.length ? (confirmed / clips.length) * 100 : 0}%`,
              }}
            />
          </div>
          <Link to="/legend-detection" className="rs-meta">
            Back to review
          </Link>
        </div>
      </header>

      <div className="rs-baseline-toolbar">
        <label className="rs-baseline-toggle">
          <Checkbox
            checked={showConfirmed}
            onChange={(event) => setShowConfirmed(event.target.checked)}
          />
          Show confirmed
        </label>
        <Button size="sm" onClick={undoLast} disabled={undo.length === 0}>
          Undo <kbd>u</kbd>
        </Button>
        {change.isError ? (
          <span className="rs-legend-error">
            Saving failed: {change.error.message}
          </span>
        ) : null}
        <span className="rs-meta rs-baseline-keys">
          <kbd>j</kbd> <kbd>k</kbd> clip · <kbd>↑</kbd> <kbd>↓</kbd> group ·{" "}
          <kbd>Enter</kbd> change · <kbd>x</kbd> can't tell · <kbd>c</kbd>{" "}
          confirm group · <kbd>1</kbd>–<kbd>6</kbd> frame
        </span>
      </div>

      {baseline.isLoading ? (
        <div className="rs-empty">Loading clips…</div>
      ) : baseline.isError ? (
        <div className="rs-empty">Clips could not be loaded.</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">There are no Apex Legends clips.</div>
      ) : (
        <div className="rs-baseline-layout">
          <div className="rs-baseline-groups">
            {groups.length === 0 ? (
              <div className="rs-empty">
                Every clip is confirmed. Tick “Show confirmed” to check them
                again.
              </div>
            ) : null}
            {groups.map((group) => (
              <section
                key={group.key}
                className={`rs-baseline-group${group === selectedGroup ? " current" : ""}`}
                aria-label={groupTitle(group)}
              >
                <header className="rs-baseline-group-heading">
                  <LegendPortrait
                    legend={group.legend}
                    legends={legends}
                    size={44}
                  />
                  <div className="rs-baseline-group-title">
                    <h2>{groupTitle(group)}</h2>
                    <div className="rs-meta">
                      {group.total} {group.total === 1 ? "clip" : "clips"} ·{" "}
                      {group.done
                        ? "All confirmed"
                        : `${group.pending.length} to confirm`}
                      {group.lowestConfidence !== null
                        ? ` · lowest ${formatConfidence(group.lowestConfidence)}`
                        : ""}
                      {group.movedCount > 0
                        ? ` · ${group.movedCount} relabelled`
                        : ""}
                    </div>
                  </div>
                  {group.pending.length > 0 ? (
                    <Button
                      variant="primary"
                      onClick={() => confirmGroup(group)}
                    >
                      Confirm {group.pending.length}
                      {group.key === noResultKey ? " as can't tell" : ""}
                    </Button>
                  ) : null}
                </header>
                {group.tiles.length > 0 ? (
                  <div className="rs-baseline-tiles">
                    {group.tiles.map((tile) => (
                      <BaselineTileButton
                        key={tile.clip.clipId}
                        tile={tile}
                        selected={tile === selected}
                        onSelect={() => select(tile.clip.clipId)}
                      />
                    ))}
                  </div>
                ) : null}
              </section>
            ))}
          </div>
          {selected ? (
            <BaselinePanel
              key={selected.clip.clipId}
              tile={selected}
              legends={legends}
              frame={frame}
              onFrame={setFrame}
              picking={picking}
              onStartPicking={() => setPicking(true)}
              onPick={relabel}
              onCancelPicking={() => setPicking(false)}
            />
          ) : null}
        </div>
      )}
      <p className="rs-meta rs-baseline-footnote">
        Results below {formatConfidence(lowConfidenceBelow)} are marked.{" "}
        {answerLabel(null)} means the owner's own legend can't be seen in any
        frame.
      </p>
    </div>
  );
}
