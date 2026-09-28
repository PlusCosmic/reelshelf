/** Three books on a shelf beside the wordmark; drawn from theme tokens so one mark serves both themes. */
export function BrandLogo() {
  return (
    <span className="rs-brand-logo" aria-hidden="true">
      <svg className="rs-brand-logo-mark" viewBox="0 0 30 30">
        <rect x="3" y="6" width="6" height="20" rx="1" fill="var(--fill)" />
        <rect x="10" y="3" width="5" height="23" rx="1" fill="var(--accent)" />
        <rect
          x="16.5"
          y="8"
          width="5"
          height="18"
          rx="1"
          fill="var(--fill)"
          transform="rotate(12 19 26)"
        />
        <rect x="1" y="26" width="28" height="2.5" rx="1" fill="var(--fg)" />
      </svg>
      <span className="rs-brand-logo-word">Reelshelf</span>
    </span>
  );
}
