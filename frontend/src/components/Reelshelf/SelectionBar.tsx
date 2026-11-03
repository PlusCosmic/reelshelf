import { IconTrash, IconX } from "@tabler/icons-react";
import { binCopy } from "@/copy/bin";

const copy = binCopy.selection;

/**
 * The bar at the foot of the library list while clips are being chosen: how many, choosing every clip that matches
 * (loaded or not), and moving them to the bin.
 */
export function SelectionBar({
  chosen,
  matching,
  loaded,
  allMatching,
  binning,
  onSelectAll,
  onBin,
  onClear,
}: {
  chosen: number;
  /** How many clips the list's filters match, loaded or not. */
  matching: number;
  loaded: number;
  allMatching: boolean;
  binning: boolean;
  onSelectAll: () => void;
  onBin: () => void;
  onClear: () => void;
}) {
  return (
    <div className="rs-selection-bar" role="region" aria-label={copy.barLabel}>
      <div className="rs-selection-count" aria-live="polite">
        <strong>
          {allMatching ? copy.allMatching(matching) : copy.selected(chosen)}
        </strong>
        {!allMatching && (chosen < matching || loaded < matching) ? (
          <button
            type="button"
            className="rs-selection-all"
            onClick={onSelectAll}
          >
            {copy.selectAll(matching)}
          </button>
        ) : null}
      </div>
      <p className="rs-selection-hint">{copy.hint}</p>
      <div className="rs-selection-actions">
        <button type="button" className="rs-small-button" onClick={onClear}>
          <IconX size={13} aria-hidden="true" />
          {copy.clear}
        </button>
        <button
          type="button"
          className="rs-small-button rs-selection-bin"
          onClick={onBin}
          disabled={chosen === 0 || binning}
        >
          <IconTrash size={13} aria-hidden="true" />
          {binning ? copy.binning : copy.bin}
        </button>
      </div>
    </div>
  );
}
