// landing.jsx — quiet, personal landing page
// No marketing copy, no feature pitches, no social proof. Just a warm
// "come in" door for the user and a few close friends.

function Landing({ onLogin }) {
  const [hover, setHover] = React.useState(false);

  // A small drift loop for the preview clips behind the door — gentle
  // sense of life without being a video.
  const [t, setT] = React.useState(0);
  React.useEffect(() => {
    let raf;
    const start = performance.now();
    const tick = () => { setT((performance.now() - start) / 1000); raf = requestAnimationFrame(tick); };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, []);

  // Pick four clips with varied game colors for the floating preview
  const previewClips = ['c01', 'c06', 'c09', 'c11'].map(id => window.CLIPS.find(c => c.id === id));

  return (
    <div style={{
      minHeight: '100vh',
      background: 'var(--bg)',
      color: 'var(--fg)',
      fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif',
      display: 'flex', flexDirection: 'column',
      position: 'relative', overflow: 'hidden',
    }}>
      {/* Soft warm wash in the corners */}
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none',
        background: 'radial-gradient(ellipse 60% 40% at 15% 10%, color-mix(in oklch, var(--accent) 8%, transparent), transparent 60%), radial-gradient(ellipse 50% 35% at 90% 90%, color-mix(in oklch, var(--accent) 6%, transparent), transparent 60%)',
      }} />

      {/* Tiny logo, top-left */}
      <header style={{ padding: '28px 32px', position: 'relative', zIndex: 2 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
          <div style={{
            width: 24, height: 24, borderRadius: 6, background: 'var(--accent)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}>
            <svg width="12" height="12" viewBox="0 0 14 14" fill="none">
              <path d="M2 2L12 7L2 12Z" fill="#fff" />
            </svg>
          </div>
          <div style={{ fontFamily: '"Instrument Serif", serif', fontSize: 20, lineHeight: 1, fontWeight: 400, letterSpacing: -0.4 }}>
            Reel<span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>shelf</span>
          </div>
        </div>
      </header>

      {/* Center: greeting + door */}
      <main style={{
        flex: 1, display: 'flex', flexDirection: 'column',
        alignItems: 'center', justifyContent: 'center',
        padding: '40px 32px 80px', position: 'relative', zIndex: 2,
      }}>
        {/* Floating preview clips, drifting */}
        <div style={{ position: 'relative', width: 520, height: 220, marginBottom: 12 }}>
          {previewClips.map((c, i) => {
            const positions = [
              { x: -200, y: 20, rot: -6, w: 180 },
              { x: -40,  y: 0,  rot: 2,  w: 200 },
              { x: 100,  y: 30, rot: -3, w: 170 },
              { x: 240,  y: 10, rot: 5,  w: 180 },
            ][i];
            const drift = Math.sin(t * 0.4 + i * 1.7) * 4;
            return (
              <div key={c.id} style={{
                position: 'absolute',
                left: '50%', top: 0,
                width: positions.w,
                transform: `translate(${positions.x}px, ${positions.y + drift}px) rotate(${positions.rot}deg)`,
                padding: 8, paddingBottom: 8,
                background: 'var(--bg)',
                borderRadius: 10,
                boxShadow: '0 12px 32px rgba(40,30,20,.18), 0 1px 3px rgba(40,30,20,.08)',
                border: '1px solid var(--line)',
                transition: 'transform 0.6s ease-out',
              }}>
                <ClipThumb clip={c} hoverPulse={false} />
              </div>
            );
          })}
        </div>

        <h1 style={{
          fontFamily: '"Instrument Serif", serif',
          fontSize: 88, fontWeight: 400,
          letterSpacing: -2, lineHeight: 1, margin: '0 0 16px',
          textAlign: 'center', textWrap: 'balance',
        }}>
          Welcome <span style={{ fontStyle: 'italic', color: 'var(--accent)' }}>back</span>.
        </h1>

        <p style={{
          fontSize: 17, lineHeight: 1.5, color: 'var(--fg-soft)',
          margin: '0 0 36px', maxWidth: 420, textAlign: 'center', textWrap: 'pretty',
        }}>
          Your shelf is exactly where you left it.
        </p>

        <button onClick={onLogin}
          onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
          style={{
            display: 'inline-flex', alignItems: 'center', gap: 11,
            background: '#5865F2', color: '#fff', border: 'none',
            padding: '14px 22px', borderRadius: 10,
            fontSize: 15, fontWeight: 600, cursor: 'pointer',
            fontFamily: 'inherit',
            boxShadow: hover
              ? '0 10px 28px rgba(88,101,242,.38)'
              : '0 4px 14px rgba(88,101,242,.22)',
            transform: hover ? 'translateY(-1px)' : 'none',
            transition: 'all .18s',
          }}>
          <svg width="22" height="22" viewBox="0 0 24 24" fill="#fff">
            <path d="M19.27 5.33C17.94 4.71 16.5 4.26 15 4a.09.09 0 0 0-.07.03c-.18.33-.39.76-.53 1.09a16.09 16.09 0 0 0-4.8 0c-.14-.34-.35-.76-.54-1.09a.1.1 0 0 0-.07-.03c-1.5.26-2.93.71-4.27 1.33a.09.09 0 0 0-.04.04C2.04 9.46 1.21 13.46 1.62 17.42a.1.1 0 0 0 .04.07c1.85 1.36 3.64 2.18 5.4 2.73a.1.1 0 0 0 .11-.04c.42-.57.79-1.17 1.11-1.81a.1.1 0 0 0-.05-.13c-.59-.22-1.15-.49-1.7-.8a.1.1 0 0 1-.01-.16l.34-.27a.1.1 0 0 1 .1-.01c3.56 1.62 7.41 1.62 10.92 0a.1.1 0 0 1 .1.01l.34.27a.1.1 0 0 1-.01.16c-.55.32-1.11.58-1.7.8a.1.1 0 0 0-.05.13c.32.64.69 1.24 1.11 1.81a.1.1 0 0 0 .11.04c1.77-.55 3.55-1.37 5.4-2.73a.1.1 0 0 0 .04-.07c.5-4.59-.78-8.56-3.5-12.05a.07.07 0 0 0-.04-.04zM8.52 15.01c-1.03 0-1.89-.95-1.89-2.12 0-1.17.84-2.12 1.89-2.12 1.06 0 1.91.96 1.89 2.12 0 1.17-.84 2.12-1.89 2.12zm6.97 0c-1.03 0-1.89-.95-1.89-2.12 0-1.17.84-2.12 1.89-2.12 1.06 0 1.91.96 1.89 2.12 0 1.17-.83 2.12-1.89 2.12z"/>
          </svg>
          Continue with Discord
        </button>
      </main>

      {/* Quiet footer */}
      <footer style={{
        padding: '24px 32px', textAlign: 'center',
        fontSize: 11, color: 'var(--fg-soft)', opacity: 0.7,
        position: 'relative', zIndex: 2, letterSpacing: 0.4,
      }}>
        for friends, kept quietly
      </footer>
    </div>
  );
}

Object.assign(window, { Landing });
