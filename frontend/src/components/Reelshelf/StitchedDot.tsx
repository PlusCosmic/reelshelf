/** The small stitched dot a book or row carries while it has unwatched clips. */
export function StitchedDot({ className }: { className?: string }) {
  return (
    <svg
      className={`rs-stitched-dot${className ? ` ${className}` : ""}`}
      viewBox="0 0 12 12"
      aria-hidden="true"
    >
      <circle cx="6" cy="6" r="2.6" fill="currentColor" />
      <circle
        cx="6"
        cy="6"
        r="5"
        fill="none"
        stroke="currentColor"
        strokeWidth="1"
        strokeDasharray="1.8 1.4"
      />
    </svg>
  );
}
