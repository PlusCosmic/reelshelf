import type { FocusEvent, PointerEvent } from "react";
import { useCallback, useEffect, useState } from "react";
import { nextBookId } from "./landingShelf";

function usePrefersReducedMotion() {
  const query = "(prefers-reduced-motion: reduce)";
  const [reduced, setReduced] = useState(
    () => typeof window !== "undefined" && window.matchMedia(query).matches,
  );
  useEffect(() => {
    const media = window.matchMedia(query);
    const onChange = () => setReduced(media.matches);
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, []);
  return reduced;
}

/** Whether `element` is on screen and the tab is showing, so nothing moves where nobody can see it. */
function useSeen(element: HTMLElement | null) {
  const [onScreen, setOnScreen] = useState(false);
  const [tabShown, setTabShown] = useState(
    () => typeof document === "undefined" || !document.hidden,
  );
  useEffect(() => {
    if (!element || typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(([entry]) =>
      setOnScreen(entry?.isIntersecting ?? false),
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);
  useEffect(() => {
    const onChange = () => setTabShown(!document.hidden);
    document.addEventListener("visibilitychange", onChange);
    return () => document.removeEventListener("visibilitychange", onChange);
  }, []);
  return onScreen && tabShown;
}

/**
 * Pulls the books on a shelf out one after another, every `dwellMs`, left to right. It holds still while the
 * pointer or keyboard focus is on the shelf, while the shelf is off screen, after the stop button, and always
 * for people who ask for less motion. Choosing a book (or putting it back) takes over for three turns.
 */
export function useShelfCycle(ids: readonly string[], dwellMs: number) {
  // Undefined until someone (or the cycle) has chosen: the first book starts out pulled out.
  const [chosen, setChosen] = useState<string | null | undefined>(undefined);
  const [holding, setHolding] = useState(false);
  const [stopped, setStopped] = useState(false);
  const [restingSince, setRestingSince] = useState<number | null>(null);
  const [shelfElement, setShelfElement] = useState<HTMLElement | null>(null);
  const reducedMotion = usePrefersReducedMotion();
  const seen = useSeen(shelfElement);

  const selectedId =
    chosen === undefined || (chosen !== null && !ids.includes(chosen))
      ? (ids[0] ?? null)
      : chosen;

  const moving =
    ids.length > 1 &&
    seen &&
    !holding &&
    !stopped &&
    !reducedMotion &&
    restingSince === null;

  useEffect(() => {
    if (!moving) return;
    const timer = window.setTimeout(
      () => setChosen(nextBookId(ids, selectedId)),
      dwellMs,
    );
    return () => window.clearTimeout(timer);
  }, [moving, ids, selectedId, dwellMs]);

  useEffect(() => {
    if (restingSince === null) return;
    const timer = window.setTimeout(() => setRestingSince(null), dwellMs * 3);
    return () => window.clearTimeout(timer);
  }, [restingSince, dwellMs]);

  const choose = useCallback((id: string | null) => {
    setChosen(id);
    setRestingSince(Date.now());
  }, []);

  return {
    selectedId,
    /** Someone chose a book: pull it out and let them look. */
    select: (id: string) => choose(id),
    /** Someone put the pulled-out book back. */
    putBack: () => choose(null),
    /** Whether the stop button has stopped the shelf. */
    stopped,
    /** Whether the shelf can move at all here; the stop button is pointless when it can't. */
    canMove: ids.length > 1 && !reducedMotion,
    toggleStopped: () => setStopped((value) => !value),
    /** Spread on the element around the shelf, to pause while it's pointed at or focused. */
    holdProps: {
      ref: setShelfElement,
      onPointerEnter: (event: PointerEvent<HTMLElement>) => {
        // A tap is a choice, and touch has no pointer to take away again.
        if (event.pointerType !== "touch") setHolding(true);
      },
      onPointerLeave: () => setHolding(false),
      // Only keyboard focus holds it: a clicked book keeps focus, and clicking already rests the shelf.
      onFocus: (event: FocusEvent<HTMLElement>) => {
        if (event.target.matches(":focus-visible")) setHolding(true);
      },
      onBlur: (event: FocusEvent<HTMLElement>) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null))
          setHolding(false);
      },
    },
  };
}
