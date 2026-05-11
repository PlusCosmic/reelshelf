// ui.jsx — shared primitives for the Clips app
// All components consume the live theme via CSS variables on document.documentElement.

// ── ClipThumb ─────────────────────────────────────────────────────────
// Placeholder visual for a clip — animated gradient using the game's two
// brand colors, with a soft vignette and a subtle "film grain" overlay.
// On hover, an inner reticle pulses (cozy preview cue without faking video).
function ClipThumb({ clip, size = 'md', style = {}, hoverPulse = true }) {
  const game = window.GAMES.find(g => g.id === clip.game);
  const [hover, setHover] = React.useState(false);
  const dur = formatDuration(clip.duration);

  return (
    <div
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        position: 'relative', width: '100%', aspectRatio: '16 / 9',
        borderRadius: 12, overflow: 'hidden',
        background: `linear-gradient(135deg, ${game.color1} 0%, ${game.color2} 100%)`,
        boxShadow: hover ? '0 6px 24px rgba(40,30,20,.18)' : '0 1px 3px rgba(40,30,20,.08)',
        transition: 'box-shadow .25s, transform .25s',
        transform: hover ? 'translateY(-1px)' : 'none',
        cursor: 'pointer',
        ...style,
      }}
    >
      {/* film grain */}
      <div style={{
        position: 'absolute', inset: 0,
        backgroundImage: 'url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%22120%22 height=%22120%22><filter id=%22n%22><feTurbulence baseFrequency=%220.9%22 numOctaves=%222%22/><feColorMatrix values=%220 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 .12 0%22/></filter><rect width=%22120%22 height=%22120%22 filter=%22url(%23n)%22/></svg>")',
        mixBlendMode: 'overlay', opacity: 0.35, pointerEvents: 'none',
      }} />
      {/* center reticle for hover-preview hint */}
      {hoverPulse && (
        <div style={{
          position: 'absolute', top: '50%', left: '50%',
          width: hover ? 56 : 44, height: hover ? 56 : 44,
          marginLeft: hover ? -28 : -22, marginTop: hover ? -28 : -22,
          borderRadius: '50%',
          background: 'rgba(255,255,255,.18)',
          backdropFilter: 'blur(8px)',
          border: '1px solid rgba(255,255,255,.4)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          transition: 'all .25s cubic-bezier(.2,.7,.3,1)',
        }}>
          <svg width="14" height="16" viewBox="0 0 14 16" fill="rgba(255,255,255,.95)">
            <path d="M0 1L13 8L0 15Z" />
          </svg>
        </div>
      )}
      {/* duration chip */}
      <div style={{
        position: 'absolute', bottom: 8, right: 8,
        background: 'rgba(20,15,10,.7)', color: '#fff',
        padding: '2px 7px', borderRadius: 4,
        fontSize: 11, fontWeight: 500, letterSpacing: 0.2,
        fontVariantNumeric: 'tabular-nums',
      }}>{dur}</div>
      {/* tag badge — show first tag */}
      {clip.tags[0] && size !== 'sm' && (
        <div style={{
          position: 'absolute', top: 8, left: 8,
          background: 'rgba(255,255,255,.92)', color: '#2a251f',
          padding: '2px 8px', borderRadius: 999,
          fontSize: 10, fontWeight: 600, letterSpacing: 0.4,
          textTransform: 'uppercase',
        }}>{clip.tags[0]}</div>
      )}
    </div>
  );
}

// ── GameSpine ─────────────────────────────────────────────────────────
// A vertical-spine "shelf" element — used in library views to evoke a
// physical archive (book spine on a shelf). Renders the game's gradient
// vertically with the game name rotated 90°.
function GameSpine({ game, count, active, onClick }) {
  return (
    <button
      onClick={onClick}
      style={{
        position: 'relative', width: 38, height: 200,
        borderRadius: 5, overflow: 'hidden',
        background: `linear-gradient(180deg, ${game.color1} 0%, ${game.color2} 100%)`,
        boxShadow: active
          ? '0 6px 20px rgba(40,30,20,.25), 0 0 0 2px var(--accent)'
          : '0 1px 4px rgba(40,30,20,.12)',
        cursor: 'pointer', flexShrink: 0,
        transform: active ? 'translateY(-4px)' : 'none',
        transition: 'all .2s',
        border: 'none', padding: 0,
      }}
    >
      <div style={{
        position: 'absolute', top: '50%', left: '50%',
        transform: 'translate(-50%, -50%) rotate(-90deg)',
        whiteSpace: 'nowrap', color: '#fff',
        fontSize: 11, fontWeight: 600, letterSpacing: 1,
        textTransform: 'uppercase',
        textShadow: '0 1px 2px rgba(0,0,0,.4)',
      }}>{game.name}</div>
      <div style={{
        position: 'absolute', bottom: 6, left: 0, right: 0,
        textAlign: 'center', color: 'rgba(255,255,255,.85)',
        fontSize: 10, fontWeight: 500, fontVariantNumeric: 'tabular-nums',
      }}>{count}</div>
    </button>
  );
}

// ── Scrubber ──────────────────────────────────────────────────────────
// A real, draggable timeline. Click anywhere to jump; drag the handle to
// scrub. Shows tick marks for tagged moments. The scrubber owns its own
// hover-preview tooltip.
function Scrubber({ duration, value, onChange, markers = [] }) {
  const trackRef = React.useRef(null);
  const [hoverPct, setHoverPct] = React.useState(null);
  const [dragging, setDragging] = React.useState(false);

  const pctFromX = (clientX) => {
    const r = trackRef.current.getBoundingClientRect();
    return Math.max(0, Math.min(1, (clientX - r.left) / r.width));
  };

  const onPointerDown = (e) => {
    setDragging(true);
    const update = (ev) => onChange(pctFromX(ev.clientX) * duration);
    update(e);
    const move = (ev) => update(ev);
    const up = () => {
      setDragging(false);
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  };

  const playedPct = (value / duration) * 100;

  return (
    <div
      ref={trackRef}
      onPointerDown={onPointerDown}
      onMouseMove={(e) => setHoverPct(pctFromX(e.clientX))}
      onMouseLeave={() => setHoverPct(null)}
      style={{
        position: 'relative', height: 28,
        cursor: 'pointer', userSelect: 'none',
        display: 'flex', alignItems: 'center',
      }}
    >
      {/* track */}
      <div style={{
        position: 'absolute', left: 0, right: 0, height: 4,
        background: 'var(--track-bg)', borderRadius: 2,
      }}>
        {/* played */}
        <div style={{
          height: '100%', width: `${playedPct}%`,
          background: 'var(--accent)', borderRadius: 2,
          transition: dragging ? 'none' : 'width .1s linear',
        }} />
        {/* hover preview */}
        {hoverPct != null && !dragging && (
          <div style={{
            position: 'absolute', top: -4, height: 12, width: 2,
            left: `${hoverPct * 100}%`, background: 'var(--accent)',
            opacity: 0.5, pointerEvents: 'none',
          }} />
        )}
        {/* markers */}
        {markers.map((m, i) => (
          <div key={i} title={m.label} style={{
            position: 'absolute', top: -3, height: 10, width: 2,
            left: `${(m.t / duration) * 100}%`,
            background: 'var(--fg)', opacity: 0.45,
            borderRadius: 1,
          }} />
        ))}
      </div>
      {/* handle */}
      <div style={{
        position: 'absolute', left: `calc(${playedPct}% - 8px)`,
        width: 16, height: 16, borderRadius: '50%',
        background: '#fff', border: '2px solid var(--accent)',
        boxShadow: dragging ? '0 0 0 6px color-mix(in oklch, var(--accent) 20%, transparent)' : '0 1px 3px rgba(0,0,0,.2)',
        transition: dragging ? 'none' : 'box-shadow .15s, left .1s linear',
        pointerEvents: 'none',
      }} />
      {/* hover time label */}
      {hoverPct != null && !dragging && (
        <div style={{
          position: 'absolute', bottom: 22,
          left: `${hoverPct * 100}%`, transform: 'translateX(-50%)',
          background: 'var(--fg)', color: 'var(--bg)',
          padding: '3px 6px', borderRadius: 4,
          fontSize: 10, fontWeight: 500, fontVariantNumeric: 'tabular-nums',
          pointerEvents: 'none', whiteSpace: 'nowrap',
        }}>{formatDuration(hoverPct * duration)}</div>
      )}
    </div>
  );
}

// ── Chip ──────────────────────────────────────────────────────────────
function Chip({ children, active, onClick, accent = false, count }) {
  return (
    <button onClick={onClick} style={{
      display: 'inline-flex', alignItems: 'center', gap: 6,
      padding: '5px 11px', borderRadius: 999,
      border: '1px solid',
      borderColor: active ? 'var(--accent)' : 'var(--line)',
      background: active ? 'color-mix(in oklch, var(--accent) 12%, var(--bg))' : 'transparent',
      color: active ? 'var(--accent-ink)' : 'var(--fg-soft)',
      fontSize: 12, fontWeight: 500,
      cursor: 'pointer', whiteSpace: 'nowrap',
      transition: 'all .15s',
      fontFamily: 'inherit',
    }}>
      {children}
      {count != null && (
        <span style={{ opacity: 0.6, fontVariantNumeric: 'tabular-nums', fontSize: 11 }}>{count}</span>
      )}
    </button>
  );
}

// ── helpers ───────────────────────────────────────────────────────────
function formatDuration(seconds) {
  const s = Math.floor(seconds % 60);
  const m = Math.floor(seconds / 60);
  const h = Math.floor(seconds / 3600);
  if (h > 0) return `${h}:${String(m % 60).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  return `${m}:${String(s).padStart(2, '0')}`;
}

function formatDate(iso) {
  const d = new Date(iso);
  const now = new Date('2026-05-04');
  const days = Math.floor((now - d) / (1000 * 60 * 60 * 24));
  if (days === 0) return 'Today';
  if (days === 1) return 'Yesterday';
  if (days < 7) return `${days}d ago`;
  if (days < 30) return `${Math.floor(days / 7)}w ago`;
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

function formatSize(mb) {
  if (mb < 1000) return `${mb} MB`;
  return `${(mb / 1024).toFixed(1)} GB`;
}

// ── Avatar ────────────────────────────────────────────────────────────
function Avatar({ name, size = 22 }) {
  const initial = name[0].toUpperCase();
  // Stable color from name
  const hue = (name.charCodeAt(0) * 37) % 360;
  return (
    <div style={{
      width: size, height: size, borderRadius: '50%',
      background: `oklch(0.72 0.08 ${hue})`,
      color: '#fff',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      fontSize: size * 0.45, fontWeight: 600,
      flexShrink: 0,
      border: '1.5px solid var(--bg)',
    }}>{initial}</div>
  );
}

// ── Icon (very small set, hand-tuned) ─────────────────────────────────
function Icon({ name, size = 16, color = 'currentColor' }) {
  const paths = {
    search: <path d="M11 11L14 14M12.5 7.5a5 5 0 1 1-10 0 5 5 0 0 1 10 0z" />,
    plus: <path d="M8 3v10M3 8h10" />,
    grid: <><rect x="2" y="2" width="5" height="5" /><rect x="9" y="2" width="5" height="5" /><rect x="2" y="9" width="5" height="5" /><rect x="9" y="9" width="5" height="5" /></>,
    list: <><path d="M2 4h12M2 8h12M2 12h12" /></>,
    play: <path d="M3 2L13 8L3 14Z" fill={color} />,
    pause: <><rect x="3" y="2" width="3.5" height="12" fill={color} /><rect x="9.5" y="2" width="3.5" height="12" fill={color} /></>,
    upload: <><path d="M8 11V3M4.5 6.5L8 3l3.5 3.5M3 13h10" /></>,
    folder: <path d="M2 4a1 1 0 0 1 1-1h3l1.5 1.5H13a1 1 0 0 1 1 1V12a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4z" />,
    tag: <path d="M8 2H3a1 1 0 0 0-1 1v5l6 6 6-6-6-6z M5.5 5.5a.5.5 0 1 1-1 0 .5.5 0 0 1 1 0z" />,
    calendar: <><rect x="2" y="3" width="12" height="11" rx="1" /><path d="M2 6h12M5 1v3M11 1v3" /></>,
    close: <path d="M3 3l10 10M13 3L3 13" />,
    chevronDown: <path d="M3 6l5 5 5-5" />,
    chevronRight: <path d="M6 3l5 5-5 5" />,
    share: <><circle cx="4" cy="8" r="2" /><circle cx="12" cy="3.5" r="2" /><circle cx="12" cy="12.5" r="2" /><path d="M5.7 7L10.3 4.5M5.7 9L10.3 11.5" /></>,
    eye: <><path d="M1 8s2.5-5 7-5 7 5 7 5-2.5 5-7 5-7-5-7-5z" /><circle cx="8" cy="8" r="2" /></>,
    sliders: <><path d="M2 4h12M2 8h12M2 12h12" /><circle cx="5" cy="4" r="1.5" fill="var(--bg)" /><circle cx="10" cy="8" r="1.5" fill="var(--bg)" /><circle cx="6" cy="12" r="1.5" fill="var(--bg)" /></>,
    sparkle: <path d="M8 2L9 6L13 7L9 8L8 12L7 8L3 7L7 6Z" fill={color} />,
  };
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke={color} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
}

Object.assign(window, { ClipThumb, GameSpine, Scrubber, Chip, Avatar, Icon, formatDuration, formatDate, formatSize });
