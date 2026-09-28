import type { ReactNode } from "react";
import { useEffect, useEffectEvent, useId } from "react";
import { IconX } from "@tabler/icons-react";

/**
 * A modal dialog in the shelf's style: eyebrow, title and a close button over the content. Escape
 * or a click outside closes it unless `busy` says a save is in flight.
 */
export function Dialog({
  eyebrow,
  title,
  busy = false,
  onClose,
  children,
}: {
  eyebrow?: string;
  title: string;
  busy?: boolean;
  onClose: () => void;
  children: (titleId: string) => ReactNode;
}) {
  const titleId = useId();
  const close = () => {
    if (!busy) onClose();
  };
  const closeOnEscape = useEffectEvent(close);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") closeOnEscape();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  return (
    <div className="rs-modal-layer" role="presentation" onMouseDown={close}>
      <section
        className="rs-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="rs-modal-header">
          <div>
            {eyebrow ? <p className="rs-eyebrow">{eyebrow}</p> : null}
            <h2 className="rs-modal-title" id={titleId}>
              {title}
            </h2>
          </div>
          <button
            className="rs-icon-button"
            type="button"
            aria-label="Close"
            onClick={close}
            disabled={busy}
          >
            <IconX size={16} />
          </button>
        </header>
        {children(titleId)}
      </section>
    </div>
  );
}
