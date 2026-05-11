// bulk-upload.jsx — fleshed-out bulk clip upload (Direction A)
// One direction now: a dense queue table that scales from drop-on-library
// straight through tagging and filing. Key additions over v1:
//   • clips group into session sub-headers (same game, recorded together)
//   • saved Macros — one tap applies tag + collection to selection
//   • sticky bulk-action bar + sticky footer count
//   • per-row hover quick-actions (game override menu, regen title, split,
//     delete) and per-row inline detection-confidence affordances
//   • a separate "drop on library" entry artboard showing the moment a
//     drag-over fires on the library itself

// ── Shared sample queue ───────────────────────────────────────────────
// Twelve in-flight clips with mixed detection states. Adjacent same-game
// clips collapse into one "session" sub-header in the table.
const QUEUE = [
  { id: 'q01', file: 'session_2026-05-04_2147.mp4', game: 'valorant', title: 'Ace on Bind retake',          tags: ['ace', 'clutch'],         duration: 38,  size: 142, status: 'ready', confidence: 94, time: '21:47' },
  { id: 'q02', file: 'session_2026-05-04_2152.mp4', game: 'valorant', title: '1v3 spike defuse',            tags: ['1v3', 'clutch'],         duration: 22,  size: 88,  status: 'ready', confidence: 91, time: '21:52' },
  { id: 'q03', file: 'session_2026-05-04_2208.mp4', game: 'valorant', title: 'Reyna ult through smoke',     tags: ['highlight'],             duration: 16,  size: 58,  status: 'ready', confidence: 88, time: '22:08' },
  { id: 'q04', file: 'session_2026-05-04_2214.mp4', game: 'valorant', title: 'Pistol round Haven',          tags: ['clutch'],                duration: 53,  size: 184, status: 'ready', confidence: 96, time: '22:14' },
  { id: 'q05', file: 'session_2026-05-04_2231.mp4', game: 'valorant', title: 'Triple kill on push',         tags: ['highlight', 'ace'],      duration: 19,  size: 72,  status: 'detecting', confidence: 0, progress: 64, time: '22:31' },
  { id: 'q06', file: 'session_2026-05-04_2247.mp4', game: 'valorant', title: '',                            tags: [],                        duration: 12,  size: 38,  status: 'detecting', confidence: 0, progress: 22, time: '22:47' },
  { id: 'q07', file: 'apex_2026-05-05_0021.mp4',    game: 'apex',     title: 'Octane jump-pad steal',       tags: ['highlight', 'lucky'],    duration: 14,  size: 52,  status: 'ready', confidence: 89, time: '00:21' },
  { id: 'q08', file: 'apex_2026-05-05_0044.mp4',    game: 'apex',     title: 'Wraith portal team save',     tags: ['clutch', 'team-wipe'],   duration: 31,  size: 110, status: 'ready', confidence: 92, time: '00:44' },
  { id: 'q09', file: 'apex_2026-05-05_0102.mp4',    game: 'apex',     title: 'Pathfinder zip steal',        tags: ['highlight'],             duration: 11,  size: 38,  status: 'detecting', confidence: 0, progress: 81, time: '01:02' },
  { id: 'q10', file: 'helldivers_2026-05-05_2301.mp4', game: 'helldiv', title: 'Hellpod into a Charger',    tags: ['fail', 'funny'],         duration: 12,  size: 38,  status: 'ready', confidence: 90, time: '23:01' },
  { id: 'q11', file: 'helldivers_2026-05-05_2317.mp4', game: 'helldiv', title: 'Friendly fire moment',      tags: ['fail', 'funny'],         duration: 28,  size: 96,  status: 'ready', confidence: 87, time: '23:17' },
  { id: 'q12', file: 'screen_recording_001.mp4',    game: null,       title: '',                            tags: [],                        duration: 47,  size: 168, status: 'unmatched', confidence: 0, time: '—' },
];

// User's saved macros — one tap applies a bundle of (tag, collection,
// optional game) to whatever rows are selected. The "Ranked night" macro
// is pinned by default; new macros are inferred from past upload patterns.
const MACROS = [
  { id: 'ranked',   name: 'Ranked night',     icon: '⌘', tags: ['ranked'],         collection: 'best-of-2026',   color: 'oklch(0.62 0.10 145)' },
  { id: 'fails',    name: 'For the fails reel', icon: '✦', tags: ['fail', 'funny'], collection: 'helldivers-fails', color: 'oklch(0.65 0.12 60)' },
  { id: 'watch',    name: 'Watch party Fri',   icon: '◐', tags: ['watch-party'],    collection: 'watch-party-fri', color: 'oklch(0.58 0.11 25)' },
];

// Returns a game record or a neutral "unknown" so unmatched clips render
// without crashing.
function gameOf(id) {
  return window.GAMES.find(g => g.id === id) || {
    id: 'unknown', name: 'Unknown', studio: '—',
    color1: 'oklch(0.55 0.01 80)', color2: 'oklch(0.32 0.01 60)',
  };
}

// Collapse adjacent same-game clips into "sessions". A session breaks on
// game change OR on a >45-minute gap (we don't have rich timestamps so
// just use game change here, but the data model leaves room).
function intoSessions(rows) {
  const out = [];
  for (const r of rows) {
    const last = out[out.length - 1];
    if (last && last.game === r.game && r.game != null) last.rows.push(r);
    else out.push({ game: r.game, rows: [r] });
  }
  return out;
}

// Filename → friendly "Session · 21:47" decorator.
function prettyFile(file) {
  const m = file.match(/(\d{2})(\d{2})\.mp4$/);
  if (!m) return file.replace('.mp4', '');
  return `Session · ${m[1]}:${m[2]}`;
}

// Page-header used by the queue page.
function BulkHeader({ step, title, subtitle }) {
  return (
    <div style={{ marginBottom: 22 }}>
      <div style={{ fontSize: 12, fontWeight: 500, letterSpacing: 1.5, textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8 }}>
        {step}
      </div>
      <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 44, fontWeight: 400, margin: 0, letterSpacing: -0.8, lineHeight: 1.05, textWrap: 'pretty' }}>
        {title}
      </h1>
      {subtitle && (
        <div style={{ fontSize: 14, color: 'var(--fg-soft)', marginTop: 10, textWrap: 'pretty', maxWidth: 640 }}>
          {subtitle}
        </div>
      )}
    </div>
  );
}

// Small reusable status dot.
function StatusDot({ status, label }) {
  const map = {
    ready:     { color: 'var(--accent)',           text: 'Ready' },
    detecting: { color: 'oklch(0.65 0.10 60)',     text: 'Detecting' },
    unmatched: { color: 'oklch(0.62 0.13 40)',     text: 'Needs game' },
    done:      { color: 'var(--fg-soft)',          text: 'Saved' },
  };
  const s = map[status] || map.ready;
  return (
    <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--fg-soft)' }}>
      <span style={{
        width: 6, height: 6, borderRadius: '50%', background: s.color,
        boxShadow: status === 'detecting' ? `0 0 0 3px color-mix(in oklch, ${s.color} 25%, transparent)` : 'none',
      }} />
      {label || s.text}
    </div>
  );
}

// Mini gradient thumb.
function MiniThumb({ gameId, w = 64, h = 36 }) {
  const g = gameOf(gameId);
  return (
    <div style={{
      width: w, height: h, borderRadius: 5,
      background: gameId
        ? `linear-gradient(135deg, ${g.color1}, ${g.color2})`
        : `repeating-linear-gradient(45deg, var(--bg) 0 6px, var(--bg-soft) 6px 12px)`,
      flexShrink: 0,
      border: gameId ? 'none' : '1px dashed var(--line)',
    }} />
  );
}

// ══════════════════════════════════════════════════════════════════════
// Queue page — the main bulk upload surface.
// ══════════════════════════════════════════════════════════════════════
function BulkUploadQueue({ onNav }) {
  const [rows, setRows] = React.useState(QUEUE);
  const [selected, setSelected] = React.useState(new Set(QUEUE.map(c => c.id)));
  const [editingTitle, setEditingTitle] = React.useState(null);
  const [collapsedSessions, setCollapsedSessions] = React.useState(new Set());
  const [hoverRow, setHoverRow] = React.useState(null);
  const [gameMenuFor, setGameMenuFor] = React.useState(null);
  const [bulkCollection, setBulkCollection] = React.useState(null);
  const [tagInput, setTagInput] = React.useState('');
  const [appliedMacros, setAppliedMacros] = React.useState(new Set());

  // Tick detecting rows forward so the page looks alive.
  React.useEffect(() => {
    const id = setInterval(() => {
      setRows(rs => rs.map(r => {
        if (r.status !== 'detecting') return r;
        const next = (r.progress || 0) + 8;
        if (next >= 100) {
          return {
            ...r, status: 'ready', progress: 100,
            confidence: 85 + Math.floor(Math.random() * 12),
            title: r.title || ['Triple-take on attacker spawn', 'Quick ace on B', 'Mid trade'][Math.floor(Math.random() * 3)],
          };
        }
        return { ...r, progress: next };
      }));
    }, 340);
    return () => clearInterval(id);
  }, []);

  const sessions = React.useMemo(() => intoSessions(rows), [rows]);
  const readyCount = rows.filter(r => r.status === 'ready').length;
  const detectCount = rows.filter(r => r.status === 'detecting').length;
  const unmatchCount = rows.filter(r => r.status === 'unmatched').length;
  const totalSize = rows.reduce((a, r) => a + r.size, 0);
  const totalDur = rows.reduce((a, r) => a + r.duration, 0);
  const allSelected = selected.size === rows.length;

  // ── Selection helpers ───────────────────────────────────────────────
  function toggleRow(id) {
    setSelected(s => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }
  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(rows.map(r => r.id)));
  }
  function toggleSession(session) {
    const ids = session.rows.map(r => r.id);
    const allInSel = ids.every(id => selected.has(id));
    setSelected(s => {
      const next = new Set(s);
      if (allInSel) ids.forEach(id => next.delete(id));
      else ids.forEach(id => next.add(id));
      return next;
    });
  }
  function toggleSessionCollapse(key) {
    setCollapsedSessions(c => {
      const n = new Set(c);
      if (n.has(key)) n.delete(key); else n.add(key);
      return n;
    });
  }

  // ── Bulk operations ─────────────────────────────────────────────────
  function applyTag(tag) {
    const t = tag.trim().replace(/^#/, '');
    if (!t) return;
    setRows(rs => rs.map(r => selected.has(r.id) && !r.tags.includes(t) ? { ...r, tags: [...r.tags, t] } : r));
  }
  function applyBulkCollection(cid) {
    setBulkCollection(cid);
    setRows(rs => rs.map(r => selected.has(r.id) ? { ...r, collection: cid } : r));
  }
  function applyMacro(m) {
    m.tags.forEach(t => applyTag(t));
    if (m.collection) applyBulkCollection(m.collection);
    setAppliedMacros(a => new Set([...a, m.id]));
  }

  // ── Per-row operations ──────────────────────────────────────────────
  function setRowGame(id, gameId) {
    setRows(rs => rs.map(r => r.id === id ? {
      ...r, game: gameId,
      status: r.status === 'unmatched' ? 'ready' : r.status,
      confidence: 100,
    } : r));
    setGameMenuFor(null);
  }
  function regenTitle(id) {
    const options = ['Untitled · ' + Date.now().toString().slice(-4), 'Late-round clutch', 'Quick frag', 'Anti-eco round'];
    const t = options[Math.floor(Math.random() * options.length)];
    setRows(rs => rs.map(r => r.id === id ? { ...r, title: t } : r));
  }
  function splitRow(id) {
    setRows(rs => {
      const i = rs.findIndex(r => r.id === id);
      if (i < 0) return rs;
      const src = rs[i];
      const a = { ...src, id: src.id + 'a', title: src.title + ' (part 1)', duration: Math.floor(src.duration / 2) };
      const b = { ...src, id: src.id + 'b', title: src.title + ' (part 2)', duration: src.duration - a.duration };
      return [...rs.slice(0, i), a, b, ...rs.slice(i + 1)];
    });
  }
  function removeRow(id) {
    setRows(rs => rs.filter(r => r.id !== id));
    setSelected(s => { const n = new Set(s); n.delete(id); return n; });
  }

  // Selected-only count of clips per macro, for the small subtitle
  const selectedCount = selected.size;

  return (
    <div style={{ maxWidth: 1180, margin: '0 auto', padding: '32px 32px 96px', position: 'relative' }}
         onClick={() => setGameMenuFor(null)}>
      <BulkHeader
        step={`Bulk upload · ${rows.length} clips queued`}
        title={<>From last night's session, <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>onto the shelf</span>.</>}
        subtitle="Auto-detection runs as each clip uploads. Edit any row inline, group by session, or use a saved Macro to tag and file the whole batch in one tap."
      />

      {/* ── Slim "drop more" strip ──────────────────────────────────── */}
      <div style={{
        border: '1.5px dashed var(--line)', borderRadius: 12,
        padding: '12px 18px', marginBottom: 18,
        display: 'flex', alignItems: 'center', gap: 14,
        background: 'var(--bg-soft)', cursor: 'pointer',
      }}>
        <div style={{ width: 32, height: 32, borderRadius: 8, background: 'var(--bg)', border: '1px solid var(--line)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <Icon name="plus" size={14} color="var(--accent)" />
        </div>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 13, fontWeight: 500 }}>Drop more clips, or click to browse</div>
          <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>{formatSize(totalSize)} · {formatDuration(totalDur)} total queued</div>
        </div>
        <div style={{ display: 'flex', gap: 14, fontSize: 12, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace' }}>
          <span><span style={{ color: 'var(--accent-ink)' }}>●</span> {readyCount} ready</span>
          <span><span style={{ color: 'oklch(0.65 0.10 60)' }}>●</span> {detectCount} detecting</span>
          {unmatchCount > 0 && <span><span style={{ color: 'oklch(0.62 0.13 40)' }}>●</span> {unmatchCount} needs game</span>}
        </div>
      </div>

      {/* ── Macros row ─────────────────────────────────────────────── */}
      <div style={{ marginBottom: 14 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 8 }}>
          <span style={{ fontSize: 11, fontWeight: 600, letterSpacing: 0.6, textTransform: 'uppercase', color: 'var(--fg-soft)' }}>
            Macros
          </span>
          <span style={{ fontSize: 11, color: 'var(--fg-soft)', fontStyle: 'italic' }}>
            One tap applies to {selectedCount} selected clip{selectedCount === 1 ? '' : 's'}
          </span>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {MACROS.map(m => {
            const applied = appliedMacros.has(m.id);
            return (
              <button
                key={m.id}
                onClick={() => applyMacro(m)}
                style={{
                  display: 'inline-flex', alignItems: 'center', gap: 10,
                  padding: '8px 12px 8px 10px', borderRadius: 10,
                  background: applied ? 'color-mix(in oklch, var(--accent) 10%, var(--bg))' : 'var(--bg)',
                  border: `1px solid ${applied ? 'var(--accent)' : 'var(--line)'}`,
                  cursor: 'pointer', fontFamily: 'inherit',
                  textAlign: 'left',
                }}
              >
                <span style={{
                  width: 26, height: 26, borderRadius: 6,
                  background: m.color, color: '#fff',
                  fontSize: 14, fontWeight: 600,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}>{m.icon}</span>
                <span>
                  <span style={{ display: 'block', fontSize: 12.5, fontWeight: 600, color: 'var(--fg)' }}>{m.name}</span>
                  <span style={{ display: 'block', fontSize: 11, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace' }}>
                    {m.tags.map(t => '#' + t).join(' ')}{m.collection && ' → ' + (window.COLLECTIONS.find(c => c.id === m.collection)?.name || '')}
                  </span>
                </span>
                {applied && <span style={{ marginLeft: 4, fontSize: 11, color: 'var(--accent-ink)' }}>✓</span>}
              </button>
            );
          })}
          <button style={{
            display: 'inline-flex', alignItems: 'center', gap: 6,
            padding: '8px 12px', borderRadius: 10,
            background: 'transparent', border: '1px dashed var(--line)',
            color: 'var(--fg-soft)', cursor: 'pointer', fontFamily: 'inherit',
            fontSize: 12,
          }}>
            <Icon name="plus" size={12} /> Save selection as macro
          </button>
        </div>
      </div>

      {/* ── Sticky bulk-action bar ────────────────────────────────── */}
      <div style={{
        position: 'sticky', top: 64, zIndex: 5,
        display: 'flex', alignItems: 'center', gap: 12, padding: '10px 14px',
        border: '1px solid var(--line)', borderRadius: 10,
        background: 'color-mix(in oklch, var(--bg) 95%, transparent)',
        backdropFilter: 'blur(10px)',
        marginBottom: 12, flexWrap: 'wrap',
        boxShadow: '0 4px 16px -10px rgba(0,0,0,0.18)',
      }}>
        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 13, color: 'var(--fg)', cursor: 'pointer', fontWeight: 500 }}>
          <input type="checkbox" checked={allSelected} onChange={toggleAll} style={{ accentColor: 'var(--accent)', width: 14, height: 14 }} />
          {allSelected ? `All ${rows.length}` : `${selected.size} of ${rows.length}`}
        </label>
        <div style={{ width: 1, height: 20, background: 'var(--line)' }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ fontSize: 12, color: 'var(--fg-soft)' }}>Tag:</span>
          <input
            value={tagInput}
            onChange={e => setTagInput(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { applyTag(tagInput); setTagInput(''); } }}
            placeholder="#ranked"
            style={{ ...bulkInput, width: 120, padding: '6px 10px', fontSize: 12 }}
          />
          <button onClick={() => { applyTag(tagInput); setTagInput(''); }} style={ghostBtn}>Apply</button>
        </div>
        <div style={{ width: 1, height: 20, background: 'var(--line)' }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12, color: 'var(--fg-soft)' }}>Collection:</span>
          <Chip active={bulkCollection === null} onClick={() => applyBulkCollection(null)}>None</Chip>
          {window.COLLECTIONS.slice(0, 2).map(c => (
            <Chip key={c.id} active={bulkCollection === c.id} onClick={() => applyBulkCollection(c.id)}>
              <Icon name="folder" size={10} /> {c.name}
            </Chip>
          ))}
          <Chip><Icon name="plus" size={10} /> New</Chip>
        </div>
        <div style={{ flex: 1 }} />
        <button onClick={() => onNav({ page: 'library' })} style={textBtn}>Cancel</button>
        <button onClick={() => onNav({ page: 'library' })} style={primaryBtn}>
          Add {selected.size} to library →
        </button>
      </div>

      {/* ── The queue table, grouped by session ──────────────────── */}
      <div style={{ border: '1px solid var(--line)', borderRadius: 10, overflow: 'visible', background: 'var(--bg)' }}>
        {/* Header */}
        <div style={{
          ...gridCols, padding: '10px 14px',
          borderBottom: '1px solid var(--line)', background: 'var(--bg-soft)',
          fontSize: 11, fontWeight: 600, letterSpacing: 0.6, textTransform: 'uppercase', color: 'var(--fg-soft)',
        }}>
          <span></span><span></span><span>Clip</span><span>Tags</span><span>Length</span><span>Status</span><span></span>
        </div>

        {sessions.map((session, si) => {
          const key = (session.game || 'unmatched') + '-' + si;
          const isCollapsed = collapsedSessions.has(key);
          const ids = session.rows.map(r => r.id);
          const allInSel = ids.every(id => selected.has(id));
          const someInSel = !allInSel && ids.some(id => selected.has(id));
          const g = gameOf(session.game);
          const sessionDur = session.rows.reduce((a, r) => a + r.duration, 0);
          const isUnmatched = !session.game;

          return (
            <React.Fragment key={key}>
              {/* ── Session sub-header ────────────────────────────── */}
              <div style={{
                display: 'flex', alignItems: 'center', gap: 12,
                padding: '10px 14px',
                background: isUnmatched
                  ? 'color-mix(in oklch, oklch(0.78 0.10 50) 8%, var(--bg))'
                  : 'color-mix(in oklch, var(--bg-soft) 70%, var(--bg))',
                borderTop: si > 0 ? '1px solid var(--line)' : 'none',
                borderBottom: !isCollapsed ? '1px solid var(--line)' : 'none',
                cursor: 'pointer',
              }}
              onClick={() => toggleSessionCollapse(key)}>
                <input
                  type="checkbox"
                  checked={allInSel}
                  ref={el => { if (el) el.indeterminate = someInSel; }}
                  onChange={() => toggleSession(session)}
                  onClick={e => e.stopPropagation()}
                  style={{ accentColor: 'var(--accent)', width: 14, height: 14 }}
                />
                <div style={{
                  width: 14, height: 14, borderRadius: 3,
                  background: session.game ? `linear-gradient(135deg, ${g.color1}, ${g.color2})` : 'repeating-linear-gradient(45deg, var(--line) 0 3px, var(--bg) 3px 6px)',
                }} />
                <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--fg)' }}>
                  {isUnmatched ? 'Unmatched' : g.name}
                </div>
                <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>
                  · {session.rows.length} clip{session.rows.length === 1 ? '' : 's'} · {formatDuration(sessionDur)}
                  {!isUnmatched && <> · session starting <span style={{ fontFamily: '"JetBrains Mono", monospace' }}>{session.rows[0].time}</span></>}
                </div>
                {isUnmatched && (
                  <div style={{ fontSize: 11, color: 'oklch(0.55 0.13 40)', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                    · pick a game to continue
                  </div>
                )}
                <div style={{ flex: 1 }} />
                <button
                  onClick={e => { e.stopPropagation(); toggleSession(session); }}
                  style={{ ...textBtn, fontSize: 11 }}
                >
                  {allInSel ? 'Deselect session' : 'Select session'}
                </button>
                <span style={{
                  display: 'inline-block', transform: isCollapsed ? 'rotate(-90deg)' : 'none',
                  transition: 'transform .15s', color: 'var(--fg-soft)',
                }}>
                  <Icon name="chevronDown" size={12} />
                </span>
              </div>

              {/* ── Rows ───────────────────────────────────────────── */}
              {!isCollapsed && session.rows.map((r, ri) => {
                const isSel = selected.has(r.id);
                const isHover = hoverRow === r.id;
                return (
                  <div
                    key={r.id}
                    onMouseEnter={() => setHoverRow(r.id)}
                    onMouseLeave={() => setHoverRow(null)}
                    style={{
                      ...gridCols, padding: '12px 14px',
                      borderBottom: ri < session.rows.length - 1 ? '1px solid var(--line)' : 'none',
                      alignItems: 'center',
                      background: isHover ? 'color-mix(in oklch, var(--accent) 3%, var(--bg))' : 'transparent',
                      opacity: r.status === 'detecting' ? 0.92 : 1,
                      position: 'relative',
                    }}
                  >
                    <input type="checkbox" checked={isSel} onChange={() => toggleRow(r.id)} style={{ accentColor: 'var(--accent)', width: 14, height: 14 }} />
                    <MiniThumb gameId={r.game} w={64} h={36} />
                    <div style={{ minWidth: 0 }}>
                      {editingTitle === r.id ? (
                        <input
                          autoFocus
                          value={r.title}
                          onChange={e => setRows(rs => rs.map(x => x.id === r.id ? { ...x, title: e.target.value } : x))}
                          onBlur={() => setEditingTitle(null)}
                          onKeyDown={e => e.key === 'Enter' && setEditingTitle(null)}
                          style={{ ...bulkInput, padding: '4px 8px', fontSize: 13 }}
                        />
                      ) : (
                        <div onClick={() => setEditingTitle(r.id)} style={{ fontSize: 13, fontWeight: 500, cursor: 'text', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                          {r.title || <span style={{ color: 'var(--fg-soft)', fontStyle: 'italic' }}>Untitled · click to name</span>}
                        </div>
                      )}
                      <div style={{ fontSize: 11, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace', marginTop: 2, display: 'flex', gap: 8, alignItems: 'center', overflow: 'hidden', whiteSpace: 'nowrap' }}>
                        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{prettyFile(r.file)} · {formatSize(r.size)}</span>
                        {r.confidence > 0 && r.game && (
                          <span style={{
                            padding: '1px 6px', borderRadius: 999, fontSize: 10,
                            background: 'color-mix(in oklch, var(--accent) 10%, transparent)',
                            color: 'var(--accent-ink)', fontWeight: 600,
                          }} title={`Game detected with ${r.confidence}% confidence`}>
                            {r.confidence}%
                          </span>
                        )}
                      </div>
                    </div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, alignItems: 'center' }}>
                      {r.tags.length === 0 && r.status !== 'detecting' && (
                        <span style={{ fontSize: 12, color: 'var(--fg-soft)', fontStyle: 'italic' }}>—</span>
                      )}
                      {r.tags.slice(0, 4).map(t => (
                        <span key={t} style={{
                          fontSize: 11, padding: '2px 7px', borderRadius: 999,
                          border: '1px solid var(--line)', color: 'var(--fg-soft)',
                        }}>#{t}</span>
                      ))}
                      {r.tags.length > 4 && <span style={{ fontSize: 11, color: 'var(--fg-soft)' }}>+{r.tags.length - 4}</span>}
                    </div>
                    <div style={{ fontSize: 12, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace' }}>
                      {formatDuration(r.duration)}
                    </div>
                    <div>
                      {r.status === 'detecting' ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                          <div style={{ height: 3, borderRadius: 2, background: 'var(--track-bg)', overflow: 'hidden' }}>
                            <div style={{ width: `${r.progress || 0}%`, height: '100%', background: 'oklch(0.65 0.10 60)', transition: 'width .3s' }} />
                          </div>
                          <StatusDot status="detecting" label={`Detecting · ${r.progress || 0}%`} />
                        </div>
                      ) : (
                        <StatusDot status={r.status} />
                      )}
                    </div>

                    {/* Hover quick-actions */}
                    <div style={{
                      display: 'flex', gap: 2, justifyContent: 'flex-end',
                      opacity: isHover ? 1 : 0, transition: 'opacity .15s',
                      pointerEvents: isHover ? 'auto' : 'none',
                    }}>
                      <RowAction title="Set game" onClick={e => { e.stopPropagation(); setGameMenuFor(gameMenuFor === r.id ? null : r.id); }} icon="tag" />
                      <RowAction title="Regenerate title" onClick={() => regenTitle(r.id)} icon="sparkle" />
                      <RowAction title="Split clip" onClick={() => splitRow(r.id)} icon="sliders" />
                      <RowAction title="Remove" onClick={() => removeRow(r.id)} icon="close" />
                    </div>

                    {/* Game-override mini-menu */}
                    {gameMenuFor === r.id && (
                      <div
                        onClick={e => e.stopPropagation()}
                        style={{
                          position: 'absolute', top: '100%', right: 14, zIndex: 10,
                          marginTop: 4, padding: 6, borderRadius: 10,
                          background: 'var(--bg)', border: '1px solid var(--line)',
                          boxShadow: '0 8px 24px -8px rgba(0,0,0,0.18)',
                          width: 180, display: 'flex', flexDirection: 'column', gap: 2,
                        }}
                      >
                        <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: 0.6, color: 'var(--fg-soft)', padding: '4px 8px 2px' }}>Override game</div>
                        {window.GAMES.slice(0, 6).map(gm => (
                          <button
                            key={gm.id}
                            onClick={() => setRowGame(r.id, gm.id)}
                            style={{
                              display: 'flex', alignItems: 'center', gap: 8,
                              padding: '6px 8px', borderRadius: 6,
                              background: r.game === gm.id ? 'color-mix(in oklch, var(--accent) 10%, transparent)' : 'transparent',
                              border: 'none', cursor: 'pointer', fontFamily: 'inherit',
                              fontSize: 12, color: 'var(--fg)', textAlign: 'left',
                            }}
                          >
                            <span style={{ width: 10, height: 10, borderRadius: 2, background: `linear-gradient(135deg, ${gm.color1}, ${gm.color2})` }} />
                            {gm.name}
                            {r.game === gm.id && <span style={{ marginLeft: 'auto', color: 'var(--accent-ink)', fontSize: 11 }}>✓</span>}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </React.Fragment>
          );
        })}
      </div>

      {/* ── Sticky bottom summary ──────────────────────────────────── */}
      <div style={{
        position: 'sticky', bottom: 16, marginTop: 24, zIndex: 4,
        padding: '10px 16px',
        border: '1px solid var(--line)', borderRadius: 12,
        background: 'color-mix(in oklch, var(--bg) 95%, transparent)',
        backdropFilter: 'blur(10px)',
        display: 'flex', alignItems: 'center', gap: 14,
        boxShadow: '0 8px 24px -10px rgba(0,0,0,0.2)',
      }}>
        <div style={{ fontSize: 13 }}>
          <span style={{ fontWeight: 600 }}>{rows.length} clips</span>
          <span style={{ color: 'var(--fg-soft)' }}>
            {' '}· {formatDuration(totalDur)} · {formatSize(totalSize)} · {readyCount} ready{detectCount ? ` · ${detectCount} still detecting` : ''}
          </span>
        </div>
        <div style={{ flex: 1 }} />
        <span style={{ fontSize: 11, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace' }}>
          ⌘⏎ to add all
        </span>
        <button onClick={() => onNav({ page: 'library' })} style={primaryBtn}>
          Add {selected.size} to library
        </button>
      </div>
    </div>
  );
}

// Tiny icon-only action button used in per-row hover affordances.
function RowAction({ icon, title, onClick }) {
  return (
    <button
      onClick={onClick}
      title={title}
      style={{
        width: 26, height: 26, borderRadius: 6, padding: 0,
        background: 'var(--bg)', border: '1px solid var(--line)',
        color: 'var(--fg-soft)', cursor: 'pointer',
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      }}
    >
      <Icon name={icon} size={12} />
    </button>
  );
}

// ══════════════════════════════════════════════════════════════════════
// Library — drop active.  Shows the moment a user drags a folder of clips
// over the library; the whole window dims and a centered drop target says
// "release to start uploading".  This is the entry path into the queue.
// ══════════════════════════════════════════════════════════════════════
function LibraryDropEntry({ onNav, cardStyle }) {
  return (
    <div style={{ position: 'relative' }}>
      {/* The library content shows through, dimmed */}
      <div style={{ filter: 'blur(2px) saturate(0.7)', pointerEvents: 'none', opacity: 0.7 }}>
        <LibraryHome onNav={onNav} cardStyle={cardStyle || 'filmstrip'} />
      </div>

      {/* Overlay */}
      <div style={{
        position: 'absolute', inset: 0,
        background: 'color-mix(in oklch, var(--bg) 60%, transparent)',
        backdropFilter: 'blur(2px)',
        display: 'flex', alignItems: 'flex-start', justifyContent: 'center',
        paddingTop: 120,
        zIndex: 100,
      }}>
        <div style={{
          width: 'min(560px, 70%)',
          padding: '36px 36px 30px',
          borderRadius: 18,
          border: '2px dashed var(--accent)',
          background: 'color-mix(in oklch, var(--accent) 8%, var(--bg))',
          textAlign: 'center',
          boxShadow: '0 30px 80px -30px rgba(0,0,0,0.25)',
        }}>
          <div style={{
            width: 72, height: 72, borderRadius: 18,
            background: 'var(--bg)', border: '1px solid var(--line)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            margin: '0 auto 18px',
          }}>
            <Icon name="upload" size={28} color="var(--accent)" />
          </div>
          <div style={{ fontFamily: '"Instrument Serif", serif', fontSize: 32, fontWeight: 400, letterSpacing: -0.4, marginBottom: 8 }}>
            Drop <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>12 clips</span> onto the shelf
          </div>
          <div style={{ fontSize: 13, color: 'var(--fg-soft)', marginBottom: 18, textWrap: 'pretty', maxWidth: 360, margin: '0 auto 18px' }}>
            Release to start uploading. We'll detect games and group by session — you can edit before saving.
          </div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap', marginBottom: 8 }}>
            {window.GAMES.slice(0, 5).map(g => (
              <div key={g.id} style={{
                width: 22, height: 22, borderRadius: 5,
                background: `linear-gradient(135deg, ${g.color1}, ${g.color2})`,
              }} title={g.name} />
            ))}
          </div>
          <div style={{ fontSize: 11, color: 'var(--fg-soft)', fontFamily: '"JetBrains Mono", monospace' }}>
            session_2026-05-04_2147.mp4 + 11 more
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Shared styles ─────────────────────────────────────────────────────
const gridCols = {
  display: 'grid',
  gridTemplateColumns: '24px 76px 1fr minmax(180px, 1.1fr) 70px 130px 130px',
  gap: 12, alignItems: 'center',
};
const bulkLabel = {
  display: 'block', fontSize: 11, fontWeight: 600, letterSpacing: 0.6,
  textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8,
};
const bulkInput = {
  width: '100%', padding: '10px 12px', borderRadius: 8,
  border: '1px solid var(--line)', background: 'var(--bg)',
  fontSize: 14, color: 'var(--fg)', outline: 'none',
  fontFamily: 'inherit', boxSizing: 'border-box',
};
const primaryBtn = {
  padding: '8px 14px', borderRadius: 8,
  background: 'var(--accent)', color: '#fff', border: 'none',
  fontSize: 13, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
};
const ghostBtn = {
  padding: '7px 12px', borderRadius: 8,
  background: 'var(--bg)', color: 'var(--fg)',
  border: '1px solid var(--line)',
  fontSize: 12, cursor: 'pointer', fontFamily: 'inherit',
  display: 'inline-flex', alignItems: 'center', gap: 5,
};
const textBtn = {
  background: 'transparent', border: 'none', color: 'var(--fg-soft)',
  fontSize: 13, cursor: 'pointer', fontFamily: 'inherit',
  display: 'inline-flex', alignItems: 'center', gap: 4,
};

// ── Standalone wrappers ───────────────────────────────────────────────
function BulkQueueStandalone({ tweaks }) {
  const [route, setRoute] = React.useState({ page: 'upload' });
  return (
    <Shell route={{ page: 'upload' }} onNav={setRoute} onUpload={() => {}}>
      <BulkUploadQueue onNav={setRoute} />
    </Shell>
  );
}
function LibraryDropStandalone({ tweaks }) {
  const [route, setRoute] = React.useState({ page: 'library' });
  return (
    <Shell route={{ page: 'library' }} onNav={setRoute} onUpload={() => {}}>
      <LibraryDropEntry onNav={setRoute} cardStyle={tweaks?.cardStyle || 'filmstrip'} />
    </Shell>
  );
}

Object.assign(window, {
  BulkUploadQueue, LibraryDropEntry,
  BulkQueueStandalone, LibraryDropStandalone,
});
