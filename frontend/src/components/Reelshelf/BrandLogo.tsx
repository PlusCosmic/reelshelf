/** The Reelshelf tile beside the wordmark; `.rs-brand-logo-mark` lifts the tile in dark theme so it stays visible. */
export function BrandLogo() {
  return (
    <span className="rs-brand-logo" aria-hidden="true">
      <svg className="rs-brand-logo-mark" viewBox="0 0 512 512">
        <rect className="tile" width="512" height="512" rx="44" />
        <rect
          className="book-dark"
          x="98"
          y="156"
          width="92"
          height="228"
          rx="12"
        />
        <rect className="band" x="98" y="188" width="92" height="8" />
        <rect className="band" x="98" y="216" width="92" height="8" />
        <rect className="band" x="98" y="350" width="92" height="8" />
        <rect
          className="book-light"
          x="206"
          y="108"
          width="92"
          height="276"
          rx="12"
        />
        <rect className="band" x="206" y="140" width="92" height="8" />
        <rect className="band" x="206" y="168" width="92" height="8" />
        <rect className="band" x="206" y="350" width="92" height="8" />
        <g transform="translate(373 271) rotate(-11.3)">
          <rect
            className="book-dark"
            x="-40"
            y="-106"
            width="80"
            height="212"
            rx="12"
          />
          <rect className="band" x="-40" y="-70" width="80" height="8" />
          <rect className="band" x="-40" y="-42" width="80" height="8" />
        </g>
        <rect
          className="shelf"
          x="58"
          y="394"
          width="394"
          height="34"
          rx="17"
        />
      </svg>
      <span className="rs-brand-logo-word">Reelshelf</span>
    </span>
  );
}
