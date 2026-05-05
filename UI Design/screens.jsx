// screens.jsx — page-level components for each screen
// LibraryHome, GamePage, ClipPlayer, CollectionPage, UploadFlow

// ── Shell (top bar + nav) ─────────────────────────────────────────────
function Shell({ children, route, onNav, onUpload }) {
  return (
    <div style={{
      minHeight: '100vh', background: 'var(--bg)', color: 'var(--fg)',
      fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif',
    }}>
      <TopBar route={route} onNav={onNav} onUpload={onUpload} />
      <main>{children}</main>
    </div>
  );
}

function TopBar({ route, onNav, onUpload }) {
  const NavLink = ({ to, children }) => {
    const active = route.page === to;
    return (
      <button onClick={() => onNav({ page: to })} style={{
        background: 'none', border: 'none', cursor: 'pointer',
        padding: '6px 0', position: 'relative',
        color: active ? 'var(--fg)' : 'var(--fg-soft)',
        fontFamily: 'inherit', fontSize: 14, fontWeight: active ? 600 : 500,
      }}>
        {children}
        {active && <div style={{ position: 'absolute', bottom: -1, left: 0, right: 0, height: 2, background: 'var(--accent)', borderRadius: 1 }} />}
      </button>
    );
  };

  return (
    <header style={{
      position: 'sticky', top: 0, zIndex: 50,
      background: 'color-mix(in oklch, var(--bg) 88%, transparent)',
      backdropFilter: 'blur(12px)',
      borderBottom: '1px solid var(--line)',
      padding: '14px 32px',
      display: 'flex', alignItems: 'center', gap: 32,
    }}>
      {/* logo */}
      <div onClick={() => onNav({ page: 'library' })} style={{ display: 'flex', alignItems: 'center', gap: 9, cursor: 'pointer' }}>
        <div style={{
          width: 26, height: 26, borderRadius: 7,
          background: 'var(--accent)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
            <path d="M2 2L12 7L2 12Z" fill="#fff" />
          </svg>
        </div>
        <div style={{ fontFamily: '"Instrument Serif", serif', fontSize: 22, lineHeight: 1, fontWeight: 400, letterSpacing: -0.5 }}>
          Reel<span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>shelf</span>
        </div>
      </div>

      <nav style={{ display: 'flex', gap: 22 }}>
        <NavLink to="library">Library</NavLink>
        <NavLink to="collections">Collections</NavLink>
        <NavLink to="upload">Upload</NavLink>
      </nav>

      <div style={{ flex: 1 }} />

      {/* search */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        background: 'var(--bg-soft)', border: '1px solid var(--line)',
        padding: '6px 12px', borderRadius: 8, width: 280,
      }}>
        <Icon name="search" size={14} color="var(--fg-soft)" />
        <input placeholder="Search clips, games, tags…" style={{
          flex: 1, border: 'none', background: 'transparent',
          outline: 'none', fontSize: 13, color: 'var(--fg)',
          fontFamily: 'inherit',
        }} />
        <kbd style={{
          fontSize: 10, fontWeight: 500, color: 'var(--fg-soft)',
          background: 'var(--bg)', border: '1px solid var(--line)',
          padding: '1px 5px', borderRadius: 3, fontFamily: 'inherit',
        }}>⌘K</kbd>
      </div>

      <button onClick={onUpload} style={{
        display: 'flex', alignItems: 'center', gap: 6,
        padding: '7px 12px', border: 'none', borderRadius: 8,
        background: 'var(--fg)', color: 'var(--bg)',
        fontSize: 13, fontWeight: 500, cursor: 'pointer',
        fontFamily: 'inherit',
      }}>
        <Icon name="plus" size={13} />
        New clip
      </button>
      <Avatar name="You" size={28} />
    </header>
  );
}

// ── Library Home ──────────────────────────────────────────────────────
function LibraryHome({ onNav, cardStyle }) {
  const [filterGame, setFilterGame] = React.useState(null);
  const [filterTag, setFilterTag] = React.useState(null);
  const [sortBy, setSortBy] = React.useState('recent');

  let clips = window.CLIPS;
  if (filterGame) clips = clips.filter(c => c.game === filterGame);
  if (filterTag) clips = clips.filter(c => c.tags.includes(filterTag));
  if (sortBy === 'longest') clips = [...clips].sort((a, b) => b.duration - a.duration);
  if (sortBy === 'most-watched') clips = [...clips].sort((a, b) => b.views - a.views);

  const totalHours = (window.CLIPS.reduce((s, c) => s + c.duration, 0) / 3600).toFixed(1);
  const totalSize = (window.CLIPS.reduce((s, c) => s + c.size, 0) / 1024).toFixed(1);

  return (
    <div>
      {/* Hero — the shelf */}
      <section style={{ padding: '40px 32px 28px', borderBottom: '1px solid var(--line)' }}>
        <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', marginBottom: 28 }}>
          <div>
            <div style={{ fontSize: 12, fontWeight: 500, letterSpacing: 1.5, textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8 }}>
              Your archive · {window.CLIPS.length} clips · {totalHours}h · {totalSize} GB
            </div>
            <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 48, lineHeight: 1.05, fontWeight: 400, letterSpacing: -1, margin: 0, maxWidth: 720 }}>
              Welcome back. <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>Three new clips</span> from last night's session are waiting.
            </h1>
          </div>
        </div>

        {/* The Shelf — game spines */}
        <div style={{ position: 'relative', paddingBottom: 16 }}>
          <div style={{ display: 'flex', gap: 6, alignItems: 'flex-end', overflowX: 'auto', paddingBottom: 14 }}>
            {window.GAMES.map(g => (
              <GameSpine key={g.id} game={g} count={g.clipCount}
                active={filterGame === g.id}
                onClick={() => onNav({ page: 'game', game: g.id })} />
            ))}
            <div style={{ flex: 1 }} />
          </div>
          {/* shelf line */}
          <div style={{
            position: 'absolute', bottom: 8, left: 0, right: 0, height: 2,
            background: 'linear-gradient(to right, transparent, var(--line), transparent)',
          }} />
          <div style={{
            position: 'absolute', bottom: 0, left: 0, right: 0, height: 8,
            background: 'linear-gradient(to bottom, color-mix(in oklch, var(--fg) 6%, transparent), transparent)',
          }} />
        </div>
      </section>

      {/* Filter bar */}
      <section style={{ padding: '20px 32px', display: 'flex', alignItems: 'center', gap: 10, borderBottom: '1px solid var(--line)', flexWrap: 'wrap' }}>
        <span style={{ fontSize: 11, fontWeight: 600, letterSpacing: 1, textTransform: 'uppercase', color: 'var(--fg-soft)', marginRight: 6 }}>Filter</span>
        <Chip active={!filterGame} onClick={() => setFilterGame(null)}>All games</Chip>
        {window.GAMES.slice(0, 5).map(g => (
          <Chip key={g.id} active={filterGame === g.id} onClick={() => setFilterGame(g.id === filterGame ? null : g.id)} count={g.clipCount}>
            {g.name}
          </Chip>
        ))}
        <span style={{ width: 1, height: 18, background: 'var(--line)', margin: '0 6px' }} />
        <Chip active={!filterTag} onClick={() => setFilterTag(null)}>All tags</Chip>
        {['ace', 'clutch', 'funny', 'fail', 'highlight'].map(t => (
          <Chip key={t} active={filterTag === t} onClick={() => setFilterTag(t === filterTag ? null : t)}>
            #{t}
          </Chip>
        ))}
        <div style={{ flex: 1 }} />
        <select value={sortBy} onChange={e => setSortBy(e.target.value)} style={{
          appearance: 'none', border: '1px solid var(--line)', background: 'transparent',
          padding: '5px 26px 5px 10px', borderRadius: 8, fontSize: 12,
          color: 'var(--fg-soft)', cursor: 'pointer', fontFamily: 'inherit',
          backgroundImage: 'url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%2210%22 height=%226%22 viewBox=%220 0 10 6%22><path fill=%22%23999%22 d=%22M0 0h10L5 6z%22/></svg>")',
          backgroundRepeat: 'no-repeat', backgroundPosition: 'right 8px center',
        }}>
          <option value="recent">Most recent</option>
          <option value="longest">Longest</option>
          <option value="most-watched">Most watched</option>
        </select>
      </section>

      {/* Clip grid */}
      <section style={{ padding: '32px' }}>
        <ClipGrid clips={clips} onClipClick={(c) => onNav({ page: 'clip', clip: c.id })} cardStyle={cardStyle} />
      </section>
    </div>
  );
}

// ── ClipGrid (responds to cardStyle) ──────────────────────────────────
function ClipGrid({ clips, onClipClick, cardStyle = 'poster' }) {
  if (cardStyle === 'filmstrip') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
        {clips.map((c, i) => <ClipFilmstrip key={c.id} clip={c} onClick={() => onClipClick(c)} index={i} />)}
      </div>
    );
  }
  if (cardStyle === 'grid') {
    return (
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 14 }}>
        {clips.map(c => <ClipCardCompact key={c.id} clip={c} onClick={() => onClipClick(c)} />)}
      </div>
    );
  }
  // poster (default)
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 24, columnGap: 24, rowGap: 36 }}>
      {clips.map(c => <ClipCardPoster key={c.id} clip={c} onClick={() => onClipClick(c)} />)}
    </div>
  );
}

function ClipCardPoster({ clip, onClick }) {
  const game = window.GAMES.find(g => g.id === clip.game);
  return (
    <div onClick={onClick} style={{ cursor: 'pointer' }}>
      <ClipThumb clip={clip} />
      <div style={{ marginTop: 12 }}>
        <div style={{ fontSize: 14, fontWeight: 600, lineHeight: 1.3, color: 'var(--fg)', marginBottom: 5, textWrap: 'pretty' }}>
          {clip.title}
        </div>
        <div style={{ fontSize: 12, color: 'var(--fg-soft)', display: 'flex', alignItems: 'center', gap: 8 }}>
          <span>{game.name}</span>
          <span style={{ width: 3, height: 3, borderRadius: '50%', background: 'var(--fg-soft)', opacity: 0.5 }} />
          <span>{formatDate(clip.date)}</span>
          <span style={{ width: 3, height: 3, borderRadius: '50%', background: 'var(--fg-soft)', opacity: 0.5 }} />
          <span>{formatSize(clip.size)}</span>
        </div>
      </div>
    </div>
  );
}

function ClipCardCompact({ clip, onClick }) {
  const game = window.GAMES.find(g => g.id === clip.game);
  return (
    <div onClick={onClick} style={{ cursor: 'pointer', display: 'flex', flexDirection: 'column', gap: 8 }}>
      <ClipThumb clip={clip} />
      <div style={{ fontSize: 13, fontWeight: 500, lineHeight: 1.3, color: 'var(--fg)', textWrap: 'pretty' }}>{clip.title}</div>
      <div style={{ fontSize: 11, color: 'var(--fg-soft)' }}>{game.name} · {formatDate(clip.date)}</div>
    </div>
  );
}

function ClipFilmstrip({ clip, onClick, index }) {
  const game = window.GAMES.find(g => g.id === clip.game);
  return (
    <div onClick={onClick} style={{
      display: 'grid', gridTemplateColumns: '60px 200px 1fr auto auto auto',
      gap: 24, alignItems: 'center', padding: '14px 0',
      borderBottom: '1px solid var(--line)',
      cursor: 'pointer',
    }}>
      <div style={{ fontFamily: '"Instrument Serif", serif', fontSize: 26, color: 'var(--fg-soft)', fontVariantNumeric: 'tabular-nums' }}>
        {String(index + 1).padStart(2, '0')}
      </div>
      <div style={{ width: 200 }}>
        <ClipThumb clip={clip} hoverPulse={false} />
      </div>
      <div>
        <div style={{ fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{clip.title}</div>
        <div style={{ fontSize: 12, color: 'var(--fg-soft)', display: 'flex', gap: 6 }}>
          {clip.tags.map(t => <span key={t}>#{t}</span>)}
        </div>
      </div>
      <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>{game.name}</div>
      <div style={{ fontSize: 12, color: 'var(--fg-soft)', fontVariantNumeric: 'tabular-nums' }}>{formatDuration(clip.duration)}</div>
      <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>{formatDate(clip.date)}</div>
    </div>
  );
}

// ── Game Page ─────────────────────────────────────────────────────────
function GamePage({ gameId, onNav, cardStyle }) {
  const game = window.GAMES.find(g => g.id === gameId);
  const clips = window.CLIPS.filter(c => c.game === gameId);
  // Group by tag
  const tagCounts = {};
  clips.forEach(c => c.tags.forEach(t => { tagCounts[t] = (tagCounts[t] || 0) + 1; }));
  const topTags = Object.entries(tagCounts).sort((a, b) => b[1] - a[1]).slice(0, 6);

  return (
    <div>
      {/* Game hero */}
      <section style={{
        position: 'relative', padding: '64px 32px 40px',
        background: `linear-gradient(135deg, ${game.color1} 0%, ${game.color2} 100%)`,
        color: '#fff', overflow: 'hidden',
      }}>
        <div style={{
          position: 'absolute', inset: 0,
          background: 'url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%22120%22 height=%22120%22><filter id=%22n%22><feTurbulence baseFrequency=%220.9%22/><feColorMatrix values=%220 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 .12 0%22/></filter><rect width=%22120%22 height=%22120%22 filter=%22url(%23n)%22/></svg>")',
          opacity: 0.25, mixBlendMode: 'overlay', pointerEvents: 'none',
        }} />
        <button onClick={() => onNav({ page: 'library' })} style={{
          background: 'rgba(255,255,255,.15)', backdropFilter: 'blur(10px)',
          color: '#fff', border: '1px solid rgba(255,255,255,.25)',
          padding: '5px 11px', borderRadius: 999, fontSize: 12,
          cursor: 'pointer', fontFamily: 'inherit', marginBottom: 20,
        }}>← Library</button>
        <div style={{ fontSize: 12, fontWeight: 500, letterSpacing: 1.5, textTransform: 'uppercase', opacity: 0.7, marginBottom: 8 }}>
          {game.studio} · {clips.length} clips
        </div>
        <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 64, lineHeight: 1, fontWeight: 400, letterSpacing: -1.5, margin: 0 }}>
          {game.name}
        </h1>
        <div style={{ marginTop: 24, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          {topTags.map(([tag, count]) => (
            <div key={tag} style={{
              padding: '4px 11px', borderRadius: 999,
              background: 'rgba(255,255,255,.18)', color: '#fff',
              fontSize: 12, fontWeight: 500,
              border: '1px solid rgba(255,255,255,.22)',
            }}>#{tag} · {count}</div>
          ))}
        </div>
      </section>

      {/* Clips */}
      <section style={{ padding: '40px 32px' }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 24 }}>
          <h2 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 32, fontWeight: 400, margin: 0, letterSpacing: -0.5 }}>
            All clips
          </h2>
          <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>
            {clips.length} clips · sorted by recent
          </div>
        </div>
        <ClipGrid clips={clips} onClipClick={(c) => onNav({ page: 'clip', clip: c.id })} cardStyle={cardStyle} />
      </section>
    </div>
  );
}

// ── Clip Player ───────────────────────────────────────────────────────
function ClipPlayer({ clipId, onNav }) {
  const clip = window.CLIPS.find(c => c.id === clipId);
  const game = window.GAMES.find(g => g.id === clip.game);
  const [playing, setPlaying] = React.useState(true);
  const [time, setTime] = React.useState(0);
  const [comment, setComment] = React.useState('');
  const [comments, setComments] = React.useState([
    { who: 'Mira', t: 4, text: 'oh no oh no', at: '2h ago' },
    { who: 'Jules', t: clip.duration * 0.6, text: 'WAIT what was that', at: '1h ago' },
    { who: 'Theo', t: clip.duration * 0.85, text: 'absolute cinema', at: '20m ago' },
  ]);

  // Tick playback
  React.useEffect(() => {
    if (!playing) return;
    const id = setInterval(() => {
      setTime(t => {
        const n = t + 0.1;
        return n >= clip.duration ? 0 : n;
      });
    }, 100);
    return () => clearInterval(id);
  }, [playing, clip.duration]);

  const otherClips = window.CLIPS.filter(c => c.game === clip.game && c.id !== clip.id).slice(0, 4);

  const addComment = () => {
    if (!comment.trim()) return;
    setComments([...comments, { who: 'You', t: time, text: comment.trim(), at: 'now' }]);
    setComment('');
  };

  return (
    <div style={{ maxWidth: 1280, margin: '0 auto', padding: '28px 32px 80px' }}>
      <button onClick={() => onNav({ page: 'library' })} style={{
        background: 'transparent', color: 'var(--fg-soft)', border: 'none',
        fontSize: 13, cursor: 'pointer', fontFamily: 'inherit', marginBottom: 16,
        padding: 0,
      }}>← Library</button>

      {/* Player */}
      <div style={{
        position: 'relative', borderRadius: 14, overflow: 'hidden',
        background: `linear-gradient(135deg, ${game.color1} 0%, ${game.color2} 100%)`,
        aspectRatio: '16 / 9', boxShadow: '0 20px 60px rgba(40,30,20,.18)',
      }}>
        <div style={{
          position: 'absolute', inset: 0,
          background: 'url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%22120%22 height=%22120%22><filter id=%22n%22><feTurbulence baseFrequency=%220.9%22/><feColorMatrix values=%220 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 .12 0%22/></filter><rect width=%22120%22 height=%22120%22 filter=%22url(%23n)%22/></svg>")',
          opacity: 0.3, mixBlendMode: 'overlay',
        }} />
        {/* center play */}
        <button onClick={() => setPlaying(p => !p)} style={{
          position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)',
          width: 80, height: 80, borderRadius: '50%',
          background: 'rgba(255,255,255,.2)', backdropFilter: 'blur(20px)',
          border: '1px solid rgba(255,255,255,.4)',
          cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
          <Icon name={playing ? 'pause' : 'play'} size={28} color="#fff" />
        </button>
      </div>

      {/* Scrubber + meta */}
      <div style={{ marginTop: 18 }}>
        <Scrubber
          duration={clip.duration}
          value={time}
          onChange={setTime}
          markers={comments.map(c => ({ t: c.t, label: `${c.who}: ${c.text}` }))}
        />
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 6 }}>
          <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            <button onClick={() => setPlaying(p => !p)} style={{
              background: 'var(--bg-soft)', border: '1px solid var(--line)',
              borderRadius: 6, width: 32, height: 32, cursor: 'pointer',
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              color: 'var(--fg)',
            }}>
              <Icon name={playing ? 'pause' : 'play'} size={13} />
            </button>
            <div style={{ fontSize: 12, fontVariantNumeric: 'tabular-nums', color: 'var(--fg-soft)' }}>
              {formatDuration(time)} <span style={{ opacity: 0.5 }}>/ {formatDuration(clip.duration)}</span>
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button style={smallBtn}><Icon name="share" size={12} /> Share</button>
            <button style={smallBtn}><Icon name="folder" size={12} /> Add to collection</button>
          </div>
        </div>
      </div>

      {/* Title + meta */}
      <div style={{ marginTop: 28, display: 'grid', gridTemplateColumns: '1fr 320px', gap: 48 }}>
        <div>
          <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 36, fontWeight: 400, margin: 0, letterSpacing: -0.5, lineHeight: 1.15, textWrap: 'pretty' }}>
            {clip.title}
          </h1>
          <div style={{ marginTop: 12, display: 'flex', gap: 14, alignItems: 'center', color: 'var(--fg-soft)', fontSize: 13 }}>
            <span style={{ fontWeight: 500, color: 'var(--fg)' }}>{game.name}</span>
            <span>{formatDate(clip.date)}</span>
            <span>{formatSize(clip.size)}</span>
            <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <Icon name="eye" size={12} /> {clip.views}
            </span>
          </div>
          <div style={{ marginTop: 14, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {clip.tags.map(t => <Chip key={t}>#{t}</Chip>)}
            <Chip><Icon name="plus" size={11} /> Add tag</Chip>
          </div>

          {/* Timestamped comments */}
          <div style={{ marginTop: 36 }}>
            <h3 style={{ fontSize: 13, fontWeight: 600, letterSpacing: 0.5, textTransform: 'uppercase', color: 'var(--fg-soft)', margin: '0 0 12px' }}>
              {comments.length} timestamped notes
            </h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {comments.map((c, i) => (
                <div key={i} style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
                  <Avatar name={c.who} size={28} />
                  <div style={{ flex: 1 }}>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}>
                      <span style={{ fontSize: 13, fontWeight: 600 }}>{c.who}</span>
                      <button onClick={() => setTime(c.t)} style={{
                        background: 'var(--bg-soft)', border: 'none', borderRadius: 4,
                        padding: '1px 6px', fontSize: 11, color: 'var(--accent-ink)',
                        fontVariantNumeric: 'tabular-nums', cursor: 'pointer',
                        fontFamily: 'inherit', fontWeight: 500,
                      }}>{formatDuration(c.t)}</button>
                      <span style={{ fontSize: 11, color: 'var(--fg-soft)' }}>{c.at}</span>
                    </div>
                    <div style={{ fontSize: 14, color: 'var(--fg)', marginTop: 2 }}>{c.text}</div>
                  </div>
                </div>
              ))}
            </div>
            <div style={{ marginTop: 20, display: 'flex', gap: 12, alignItems: 'center' }}>
              <Avatar name="You" size={28} />
              <div style={{
                flex: 1, display: 'flex', alignItems: 'center', gap: 8,
                border: '1px solid var(--line)', borderRadius: 8, padding: '6px 10px',
                background: 'var(--bg-soft)',
              }}>
                <span style={{
                  fontSize: 11, fontWeight: 600, color: 'var(--accent-ink)',
                  background: 'color-mix(in oklch, var(--accent) 15%, var(--bg))',
                  padding: '2px 6px', borderRadius: 4, fontVariantNumeric: 'tabular-nums',
                }}>@{formatDuration(time)}</span>
                <input value={comment} onChange={e => setComment(e.target.value)}
                  onKeyDown={e => e.key === 'Enter' && addComment()}
                  placeholder="Add a note at this moment…"
                  style={{ flex: 1, border: 'none', background: 'transparent', outline: 'none', fontSize: 13, fontFamily: 'inherit' }} />
                <button onClick={addComment} style={{
                  background: 'var(--accent)', color: '#fff', border: 'none',
                  padding: '4px 10px', borderRadius: 5, fontSize: 12, fontWeight: 500,
                  cursor: 'pointer', fontFamily: 'inherit',
                }}>Post</button>
              </div>
            </div>
          </div>
        </div>

        {/* Sidebar — co-watch + more */}
        <aside>
          <div style={{
            border: '1px solid var(--line)', borderRadius: 12, padding: 16,
            background: 'var(--bg-soft)',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
              <Icon name="sparkle" size={14} color="var(--accent)" />
              <span style={{ fontSize: 12, fontWeight: 600, letterSpacing: 0.5, textTransform: 'uppercase' }}>Watch party</span>
            </div>
            <div style={{ fontSize: 14, color: 'var(--fg)', marginBottom: 12, textWrap: 'pretty' }}>
              Watch this clip together. Synced playback, shared chat.
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: -6, marginBottom: 12 }}>
              {['Mira', 'Jules', 'Theo'].map((n, i) => (
                <div key={n} style={{ marginLeft: i === 0 ? 0 : -6 }}><Avatar name={n} size={26} /></div>
              ))}
              <span style={{ marginLeft: 8, fontSize: 12, color: 'var(--fg-soft)' }}>3 friends online</span>
            </div>
            <button style={{
              width: '100%', background: 'var(--accent)', color: '#fff',
              border: 'none', padding: '8px 12px', borderRadius: 8,
              fontSize: 13, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
            }}>Start watch party</button>
          </div>

          <div style={{ marginTop: 24 }}>
            <h3 style={{ fontSize: 12, fontWeight: 600, letterSpacing: 1, textTransform: 'uppercase', color: 'var(--fg-soft)', margin: '0 0 14px' }}>
              More from {game.name}
            </h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {otherClips.map(c => (
                <div key={c.id} onClick={() => onNav({ page: 'clip', clip: c.id })}
                  style={{ display: 'grid', gridTemplateColumns: '120px 1fr', gap: 12, cursor: 'pointer' }}>
                  <ClipThumb clip={c} hoverPulse={false} />
                  <div>
                    <div style={{ fontSize: 13, fontWeight: 500, lineHeight: 1.3, textWrap: 'pretty' }}>{c.title}</div>
                    <div style={{ fontSize: 11, color: 'var(--fg-soft)', marginTop: 2 }}>{formatDate(c.date)}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}

const smallBtn = {
  display: 'inline-flex', alignItems: 'center', gap: 5,
  background: 'transparent', border: '1px solid var(--line)',
  padding: '5px 10px', borderRadius: 6, fontSize: 12,
  color: 'var(--fg-soft)', cursor: 'pointer', fontFamily: 'inherit',
};

// ── Collections Page ──────────────────────────────────────────────────
function CollectionsPage({ onNav }) {
  return (
    <div style={{ padding: '40px 32px' }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 32 }}>
        <div>
          <div style={{ fontSize: 12, fontWeight: 500, letterSpacing: 1.5, textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8 }}>
            Collections · {window.COLLECTIONS.length}
          </div>
          <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 48, fontWeight: 400, margin: 0, letterSpacing: -1 }}>
            Curated <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>shelves</span> & shared playlists
          </h1>
        </div>
        <button style={{
          display: 'flex', alignItems: 'center', gap: 6,
          background: 'var(--fg)', color: 'var(--bg)', border: 'none',
          padding: '8px 14px', borderRadius: 8, fontSize: 13, fontWeight: 500,
          cursor: 'pointer', fontFamily: 'inherit',
        }}>
          <Icon name="plus" size={13} /> New collection
        </button>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))', gap: 24 }}>
        {window.COLLECTIONS.map(coll => <CollectionCard key={coll.id} coll={coll} onNav={onNav} />)}
      </div>
    </div>
  );
}

function CollectionCard({ coll, onNav }) {
  const coverClips = coll.cover.map(id => window.CLIPS.find(c => c.id === id)).filter(Boolean);
  const total = window.CLIPS.filter(c => c.collection === coll.id).length || coverClips.length;
  return (
    <div style={{
      border: '1px solid var(--line)', borderRadius: 14, overflow: 'hidden',
      background: 'var(--bg)', cursor: 'pointer',
      transition: 'box-shadow .2s, transform .2s',
    }}
      onMouseEnter={e => { e.currentTarget.style.boxShadow = '0 12px 32px rgba(40,30,20,.10)'; e.currentTarget.style.transform = 'translateY(-2px)'; }}
      onMouseLeave={e => { e.currentTarget.style.boxShadow = ''; e.currentTarget.style.transform = ''; }}
    >
      {/* mosaic cover */}
      <div style={{
        display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 2,
        aspectRatio: '2 / 1', background: 'var(--line)',
      }}>
        {coverClips.slice(0, 4).map(c => (
          <div key={c.id} style={{
            background: `linear-gradient(135deg, ${window.GAMES.find(g => g.id === c.game).color1}, ${window.GAMES.find(g => g.id === c.game).color2})`,
            position: 'relative',
          }}>
            <div style={{
              position: 'absolute', inset: 0,
              background: 'url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%22120%22 height=%22120%22><filter id=%22n%22><feTurbulence baseFrequency=%220.9%22/><feColorMatrix values=%220 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 .12 0%22/></filter><rect width=%22120%22 height=%22120%22 filter=%22url(%23n)%22/></svg>")',
              opacity: 0.3, mixBlendMode: 'overlay',
            }} />
          </div>
        ))}
      </div>
      <div style={{ padding: 18 }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12, marginBottom: 6 }}>
          <h3 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 22, fontWeight: 400, margin: 0, letterSpacing: -0.3, textWrap: 'pretty' }}>
            {coll.name}
          </h3>
          <div style={{ fontSize: 11, color: 'var(--fg-soft)', fontVariantNumeric: 'tabular-nums', flexShrink: 0, paddingTop: 6 }}>
            {total} clips
          </div>
        </div>
        <div style={{ fontSize: 13, color: 'var(--fg-soft)', marginBottom: 14, textWrap: 'pretty' }}>{coll.note}</div>
        {coll.shared.length > 0 ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <div style={{ display: 'flex' }}>
              {coll.shared.slice(0, 4).map((n, i) => (
                <div key={n} style={{ marginLeft: i === 0 ? 0 : -6 }}><Avatar name={n} size={22} /></div>
              ))}
            </div>
            <div style={{ fontSize: 11, color: 'var(--fg-soft)' }}>
              Shared with {coll.shared.slice(0, 2).join(', ')}{coll.shared.length > 2 ? ` +${coll.shared.length - 2}` : ''}
            </div>
          </div>
        ) : (
          <div style={{ fontSize: 11, color: 'var(--fg-soft)', display: 'flex', alignItems: 'center', gap: 5 }}>
            <Icon name="folder" size={11} /> Private
          </div>
        )}
      </div>
    </div>
  );
}

// ── Upload Flow ───────────────────────────────────────────────────────
function UploadFlow({ onNav }) {
  const [step, setStep] = React.useState(1); // 1: drop, 2: detecting, 3: tag
  const [progress, setProgress] = React.useState(0);
  const [detected, setDetected] = React.useState(null);
  const [title, setTitle] = React.useState('');
  const [tags, setTags] = React.useState([]);
  const [collection, setCollection] = React.useState(null);
  const [dragOver, setDragOver] = React.useState(false);

  // Simulated detection
  React.useEffect(() => {
    if (step !== 2) return;
    let t = 0;
    const id = setInterval(() => {
      t += 7;
      setProgress(Math.min(100, t));
      if (t >= 100) {
        clearInterval(id);
        setDetected({ game: 'valorant', suggestedTitle: 'Ace on Bind retake', suggestedTags: ['ace', 'highlight', 'clutch'] });
        setTitle('Ace on Bind retake');
        setTags(['ace']);
        setStep(3);
      }
    }, 90);
    return () => clearInterval(id);
  }, [step]);

  return (
    <div style={{ maxWidth: 760, margin: '0 auto', padding: '56px 32px 80px' }}>
      <div style={{ fontSize: 12, fontWeight: 500, letterSpacing: 1.5, textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8 }}>
        Step {step} of 3 · Upload
      </div>
      <h1 style={{ fontFamily: '"Instrument Serif", serif', fontSize: 44, fontWeight: 400, margin: '0 0 36px', letterSpacing: -0.8, lineHeight: 1.1, textWrap: 'pretty' }}>
        {step === 1 && <>Drop a clip <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>onto the shelf</span></>}
        {step === 2 && <>Reading the clip…</>}
        {step === 3 && <>Almost there. <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>A few details.</span></>}
      </h1>

      {/* Step indicator */}
      <div style={{ display: 'flex', gap: 4, marginBottom: 36 }}>
        {[1, 2, 3].map(n => (
          <div key={n} style={{
            flex: 1, height: 3, borderRadius: 2,
            background: n <= step ? 'var(--accent)' : 'var(--line)',
            transition: 'background .3s',
          }} />
        ))}
      </div>

      {step === 1 && (
        <div
          onDragOver={e => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={e => { e.preventDefault(); setDragOver(false); setStep(2); }}
          onClick={() => setStep(2)}
          style={{
            border: `2px dashed ${dragOver ? 'var(--accent)' : 'var(--line)'}`,
            borderRadius: 16, padding: '64px 32px',
            textAlign: 'center', cursor: 'pointer',
            background: dragOver ? 'color-mix(in oklch, var(--accent) 6%, var(--bg))' : 'var(--bg-soft)',
            transition: 'all .2s',
          }}
        >
          <div style={{
            width: 64, height: 64, borderRadius: 16,
            background: 'var(--bg)', border: '1px solid var(--line)',
            margin: '0 auto 20px',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}>
            <Icon name="upload" size={26} color="var(--accent)" />
          </div>
          <div style={{ fontSize: 16, fontWeight: 500, marginBottom: 6 }}>
            Drop a video, or click to browse
          </div>
          <div style={{ fontSize: 13, color: 'var(--fg-soft)', textWrap: 'pretty', maxWidth: 380, margin: '0 auto' }}>
            MP4, MOV, or WebM up to 4 GB. We'll auto-detect the game and suggest tags.
          </div>
          <div style={{ marginTop: 24, display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
            {window.GAMES.slice(0, 5).map(g => (
              <div key={g.id} style={{
                width: 24, height: 24, borderRadius: 5,
                background: `linear-gradient(135deg, ${g.color1}, ${g.color2})`,
              }} title={g.name} />
            ))}
            <div style={{ fontSize: 11, color: 'var(--fg-soft)', alignSelf: 'center' }}>
              auto-detects 8 games
            </div>
          </div>
        </div>
      )}

      {step === 2 && (
        <div style={{
          border: '1px solid var(--line)', borderRadius: 16, padding: 32,
          background: 'var(--bg-soft)',
        }}>
          <div style={{ display: 'flex', gap: 18, alignItems: 'center', marginBottom: 20 }}>
            <div style={{ width: 96, aspectRatio: '16/9', borderRadius: 8, background: 'linear-gradient(135deg, oklch(0.55 0.18 20), oklch(0.32 0.10 350))', flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 14, fontWeight: 500, marginBottom: 4 }}>session_2026-05-04_2147.mp4</div>
              <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>248 MB · 1080p · 38s</div>
            </div>
          </div>
          <div style={{
            height: 6, borderRadius: 3, background: 'var(--track-bg)', overflow: 'hidden', marginBottom: 12,
          }}>
            <div style={{ width: `${progress}%`, height: '100%', background: 'var(--accent)', transition: 'width .2s' }} />
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: 'var(--fg-soft)' }}>
            <span>
              {progress < 30 && 'Scanning audio waveform…'}
              {progress >= 30 && progress < 60 && 'Matching HUD elements…'}
              {progress >= 60 && progress < 90 && 'Detecting game · Valorant'}
              {progress >= 90 && 'Suggesting tags…'}
            </span>
            <span style={{ fontVariantNumeric: 'tabular-nums' }}>{progress}%</span>
          </div>
        </div>
      )}

      {step === 3 && detected && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
          <div style={{
            display: 'flex', gap: 16, padding: 14,
            background: 'color-mix(in oklch, var(--accent) 8%, var(--bg))',
            border: '1px solid color-mix(in oklch, var(--accent) 30%, transparent)',
            borderRadius: 12,
          }}>
            <div style={{ width: 120, aspectRatio: '16/9', borderRadius: 6, background: 'linear-gradient(135deg, oklch(0.55 0.18 20), oklch(0.32 0.10 350))', flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 11, fontWeight: 600, letterSpacing: 0.5, textTransform: 'uppercase', color: 'var(--accent-ink)', marginBottom: 4 }}>
                Detected · 94% confidence
              </div>
              <div style={{ fontSize: 16, fontWeight: 600, marginBottom: 2 }}>Valorant</div>
              <div style={{ fontSize: 12, color: 'var(--fg-soft)' }}>HUD match · audio fingerprint · 3 highlight moments found</div>
            </div>
          </div>

          <div>
            <label style={fieldLabel}>Title</label>
            <input value={title} onChange={e => setTitle(e.target.value)} style={fieldInput} />
          </div>

          <div>
            <label style={fieldLabel}>Tags <span style={{ color: 'var(--fg-soft)', fontWeight: 400, marginLeft: 6 }}>· suggested</span></label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 8 }}>
              {detected.suggestedTags.map(t => (
                <Chip key={t} active={tags.includes(t)} onClick={() => setTags(ts => ts.includes(t) ? ts.filter(x => x !== t) : [...ts, t])}>
                  {tags.includes(t) ? '✓ ' : '+ '}#{t}
                </Chip>
              ))}
              <Chip><Icon name="plus" size={11} /> Custom tag</Chip>
            </div>
          </div>

          <div>
            <label style={fieldLabel}>Add to collection</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 8 }}>
              <Chip active={collection === null} onClick={() => setCollection(null)}>None</Chip>
              {window.COLLECTIONS.map(c => (
                <Chip key={c.id} active={collection === c.id} onClick={() => setCollection(c.id)}>
                  <Icon name="folder" size={11} /> {c.name}
                </Chip>
              ))}
            </div>
          </div>

          <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
            <button onClick={() => onNav({ page: 'library' })} style={{
              flex: 1, padding: '12px', borderRadius: 10,
              background: 'var(--accent)', color: '#fff', border: 'none',
              fontSize: 14, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
            }}>Add to library</button>
            <button onClick={() => onNav({ page: 'library' })} style={{
              padding: '12px 18px', borderRadius: 10,
              background: 'transparent', color: 'var(--fg-soft)',
              border: '1px solid var(--line)',
              fontSize: 14, cursor: 'pointer', fontFamily: 'inherit',
            }}>Cancel</button>
          </div>
        </div>
      )}
    </div>
  );
}

const fieldLabel = {
  display: 'block', fontSize: 11, fontWeight: 600, letterSpacing: 0.6,
  textTransform: 'uppercase', color: 'var(--fg-soft)', marginBottom: 8,
};
const fieldInput = {
  width: '100%', padding: '10px 12px', borderRadius: 8,
  border: '1px solid var(--line)', background: 'var(--bg)',
  fontSize: 14, color: 'var(--fg)', outline: 'none',
  fontFamily: 'inherit', boxSizing: 'border-box',
};

Object.assign(window, { Shell, LibraryHome, GamePage, ClipPlayer, CollectionsPage, UploadFlow });
