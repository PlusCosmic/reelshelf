import { useEffect, useState, type CSSProperties } from "react";
import { Link } from "@tanstack/react-router";
import { IconExternalLink } from "@tabler/icons-react";
import { Button, Input } from "@/components/ui";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { legendReferenceSheetUrl } from "@/shared/services/legendDetection";
import { formatConfidence } from "./legendReview";
import {
  lowConfidenceBelow,
  matchLegends,
  type BaselineTile,
} from "./legendBaseline";

/**
 * A legend's portrait, cut out of the reference sheet the model is sent: its legends sit in name order, half on
 * each of two rows. A null legend shows a blank card.
 */
export function LegendPortrait({
  legend,
  legends,
  size = 40,
}: {
  legend: string | null;
  legends: string[];
  size?: number;
}) {
  const index = legend ? legends.indexOf(legend) : -1;
  if (index < 0)
    return (
      <span
        className="rs-baseline-portrait is-blank"
        style={{ width: size, height: size }}
        aria-hidden="true"
      >
        ?
      </span>
    );
  const columns = Math.ceil(legends.length / 2);
  const column = index % columns;
  const row = Math.floor(index / columns);
  const style: CSSProperties = {
    width: size,
    height: size,
    backgroundImage: `url(${legendReferenceSheetUrl})`,
    backgroundSize: `${columns * 100}% 200%`,
    backgroundPosition: `${(column / (columns - 1)) * 100}% ${row * 100}%`,
  };
  return (
    <span className="rs-baseline-portrait" style={style} aria-hidden="true" />
  );
}

/**
 * The bottom-left squad panel of a 16:9 frame, cut out in the browser with the same region the model's close-up
 * uses (see LegendHudCropper), so a whole group's portraits can be compared at a glance.
 */
export function HudCrop({
  src,
  className,
}: {
  src: string;
  className?: string;
}) {
  return (
    <span
      className={`rs-baseline-hud${className ? ` ${className}` : ""}`}
      style={{ backgroundImage: `url(${src})` }}
      aria-hidden="true"
    />
  );
}

export function answerLabel(answer: string | null) {
  return answer ?? "Can't tell";
}

/** One clip in a group. Hovering steps through its frames, in case the first shows a spectated teammate. */
export function BaselineTileButton({
  tile,
  selected,
  onSelect,
}: {
  tile: BaselineTile;
  selected: boolean;
  onSelect: () => void;
}) {
  const frames = tile.clip.frameUrls;
  const [frame, setFrame] = useState(0);
  const [hovering, setHovering] = useState(false);
  useEffect(() => {
    if (!hovering || frames.length < 2) return;
    const timer = window.setInterval(
      () => setFrame((current) => (current + 1) % frames.length),
      650,
    );
    return () => window.clearInterval(timer);
  }, [hovering, frames.length]);

  const low = tile.confidence !== null && tile.confidence < lowConfidenceBelow;
  const classes = [
    "rs-baseline-tile",
    selected && "selected",
    tile.moved && "moved",
    tile.confirmed && !tile.moved && "confirmed",
  ]
    .filter(Boolean)
    .join(" ");
  return (
    <button
      type="button"
      className={classes}
      data-baseline-clip={tile.clip.clipId}
      aria-current={selected ? "true" : undefined}
      aria-label={`${tile.clip.title}: ${answerLabel(tile.answer)}`}
      onClick={onSelect}
      onMouseEnter={() => setHovering(true)}
      onMouseLeave={() => {
        setHovering(false);
        setFrame(0);
      }}
    >
      {frames.length > 0 ? (
        <HudCrop src={frames[hovering ? frame : 0]} />
      ) : (
        <span className="rs-baseline-hud is-missing" />
      )}
      <span className="rs-baseline-tile-meta">
        <span className="rs-baseline-tile-answer">
          {tile.moved ? "→ " : ""}
          {answerLabel(tile.answer)}
        </span>
        <span className={`rs-baseline-tile-confidence${low ? " low" : ""}`}>
          {tile.confirmed
            ? "✓"
            : tile.confidence !== null
              ? formatConfidence(tile.confidence)
              : tile.source === "none"
                ? "no result"
                : ""}
        </span>
      </span>
    </button>
  );
}

/** Picks a legend by typing a few letters: Enter takes the first match, Escape gives up. */
export function LegendPicker({
  legends,
  onPick,
  onCancel,
}: {
  legends: string[];
  onPick: (legend: string | null) => void;
  onCancel: () => void;
}) {
  const [query, setQuery] = useState("");
  const matches = matchLegends(legends, query);
  return (
    <div className="rs-baseline-picker">
      <label className="rs-baseline-picker-label">
        <span className="rs-meta">
          Type a few letters · Enter picks the first · Esc cancels
        </span>
        <Input
          autoFocus
          value={query}
          placeholder="val, mag, hor…"
          aria-label="Legend"
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              onCancel();
            } else if (event.key === "Enter") {
              event.preventDefault();
              if (matches.length > 0) onPick(matches[0]);
            }
          }}
        />
      </label>
      <div className="rs-baseline-picker-options">
        {matches.map((legend, index) => (
          <button
            key={legend}
            type="button"
            className={`rs-baseline-picker-option${index === 0 && query ? " first" : ""}`}
            onClick={() => onPick(legend)}
          >
            <LegendPortrait legend={legend} legends={legends} size={28} />
            {legend}
          </button>
        ))}
        <button
          type="button"
          className="rs-baseline-picker-option"
          onClick={() => onPick(null)}
        >
          <LegendPortrait legend={null} legends={legends} size={28} />
          Can't tell
        </button>
      </div>
    </div>
  );
}

/** The selected clip: its close-up and full frame, every frame to step through, and its answer to change. */
export function BaselinePanel({
  tile,
  legends,
  frame,
  onFrame,
  picking,
  onStartPicking,
  onPick,
  onCancelPicking,
}: {
  tile: BaselineTile;
  legends: string[];
  frame: number;
  onFrame: (frame: number) => void;
  picking: boolean;
  onStartPicking: () => void;
  onPick: (legend: string | null) => void;
  onCancelPicking: () => void;
}) {
  const { clip } = tile;
  const src = clip.frameUrls[frame] ?? clip.frameUrls[0];
  const source =
    tile.source === "label"
      ? `Labelled ${formatDate(clip.label!.labelledAt)}`
      : tile.source === "result"
        ? `Current result · ${clip.result!.model}${tile.confidence !== null ? ` · ${formatConfidence(tile.confidence)}` : ""}${clip.result!.hudDetected === false ? " · HUD not seen" : ""}`
        : "No result yet: pick a legend";
  return (
    <aside className="rs-baseline-panel" aria-label="Selected clip">
      <div className="rs-baseline-panel-heading">
        <div>
          <strong>{clip.title}</strong>
          <div className="rs-meta">
            {clip.ownerName} · {formatDate(clip.createdAt)}
          </div>
        </div>
        <Link
          to="/legend-detection"
          search={{ clip: clip.clipId }}
          className="rs-legend-open-clip"
          aria-label="Open in review"
          title="Open in review"
        >
          <IconExternalLink size={16} />
        </Link>
      </div>
      {src ? (
        <>
          <HudCrop src={src} className="large" />
          <img className="rs-baseline-panel-frame" src={src} alt="" />
        </>
      ) : null}
      <div className="rs-baseline-frames">
        {clip.frameUrls.map((url, index) => (
          <button
            key={url}
            type="button"
            className={`rs-baseline-frame${index === frame ? " selected" : ""}`}
            aria-label={`Frame ${index + 1}`}
            aria-pressed={index === frame}
            onClick={() => onFrame(index)}
          >
            <img src={url} alt="" loading="lazy" />
            <span>{index + 1}</span>
          </button>
        ))}
      </div>
      <div className="rs-baseline-answer">
        <LegendPortrait legend={tile.answer} legends={legends} size={48} />
        <div>
          <div className="rs-eyebrow">Answer</div>
          <div className="rs-baseline-answer-name">
            {answerLabel(tile.answer)}
          </div>
          <div className="rs-meta">{source}</div>
        </div>
      </div>
      {picking ? (
        <LegendPicker
          legends={legends}
          onPick={onPick}
          onCancel={onCancelPicking}
        />
      ) : (
        <div className="rs-baseline-actions">
          <Button onClick={onStartPicking}>
            Change legend <kbd>Enter</kbd>
          </Button>
          <Button onClick={() => onPick(null)}>
            Can't tell <kbd>x</kbd>
          </Button>
        </div>
      )}
      <p className="rs-meta rs-baseline-panel-note">
        A change saves at once and moves to the next clip. Confirming a group
        saves the rest of it as shown.
      </p>
    </aside>
  );
}
