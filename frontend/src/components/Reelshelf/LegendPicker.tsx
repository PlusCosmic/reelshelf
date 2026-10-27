import { useEffect, useRef, useState } from "react";
import type { CSSProperties } from "react";
import { IconChevronDown, IconUser, IconX } from "@tabler/icons-react";
import type { LegendCount } from "@/api-client";
import { LegendPlate } from "./ReelshelfPrimitives";

function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}

/**
 * The Apex book's legend filter: a button beside the search that opens a grid of the legends the owner
 * has played, as portraits, most played first. Picking one filters the list; the button then shows it,
 * with its own button to clear it.
 */
export function LegendPicker({
  legends,
  value,
  onChange,
  cloth,
}: {
  legends: LegendCount[];
  value: string | null;
  onChange: (legend: string | null) => void;
  /** The book's cloth, for the plates' underline. */
  cloth?: string;
}) {
  const [open, setOpen] = useState(false);
  const pickerRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const gridRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    // Start on the legend being shown, else the most played, so the keyboard lands somewhere useful.
    const grid = gridRef.current;
    const start =
      grid?.querySelector<HTMLButtonElement>('[aria-pressed="true"]') ??
      grid?.querySelector<HTMLButtonElement>("button");
    start?.focus();
    const onPointerDown = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const legendCount = plural(legends.length, "legend", "legends");
  const choose = (legend: string | null) => {
    setOpen(false);
    onChange(legend);
    triggerRef.current?.focus();
  };

  return (
    <div
      className={`rs-legend-picker${value ? " picked" : ""}`}
      ref={pickerRef}
      style={cloth ? ({ "--cloth": cloth } as CSSProperties) : undefined}
    >
      <button
        type="button"
        ref={triggerRef}
        className="rs-legend-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls="rs-legend-panel"
        aria-label={
          value ? `Played as ${value}. Change legend` : "Filter by legend"
        }
        onClick={() => setOpen((current) => !current)}
      >
        {value ? (
          <LegendPlate legend={value} cloth={cloth} size="small" />
        ) : (
          <IconUser size={15} aria-hidden="true" />
        )}
        {value ?? "Any legend"}
        <IconChevronDown size={14} aria-hidden="true" />
      </button>
      {value ? (
        <button
          type="button"
          className="rs-legend-clear"
          aria-label="Show every legend"
          onClick={() => choose(null)}
        >
          <IconX size={13} aria-hidden="true" />
        </button>
      ) : null}

      {open ? (
        <>
          <div
            className="rs-legend-backdrop"
            aria-hidden="true"
            onClick={() => setOpen(false)}
          />
          <div
            id="rs-legend-panel"
            className="rs-legend-panel"
            role="dialog"
            aria-label="Played as"
          >
            <div className="rs-legend-panel-head">
              <span className="rs-display">Played as</span>
              <span className="rs-legend-panel-count">
                {legendCount}, most played first
              </span>
              <button
                type="button"
                className="rs-legend-any"
                aria-pressed={!value}
                onClick={() => choose(null)}
              >
                Any legend
              </button>
            </div>
            <div className="rs-legend-grid" ref={gridRef}>
              {legends.map((item) => (
                <button
                  key={item.legend}
                  type="button"
                  className="rs-legend-tile"
                  aria-pressed={value === item.legend}
                  onClick={() => choose(item.legend)}
                >
                  <LegendPlate legend={item.legend} cloth={cloth} size="tile" />
                  <span className="rs-legend-tile-name">{item.legend}</span>
                  <span className="rs-legend-tile-count">
                    {plural(item.count, "clip", "clips")}
                  </span>
                </button>
              ))}
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
