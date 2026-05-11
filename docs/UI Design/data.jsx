// data.jsx — sample library content
// All games are PC-focused. Clip thumbnails are rendered as gradient placeholders
// (no faked screenshots) but every clip has plausible metadata so filters work.

const GAMES = [
  { id: 'valorant', name: 'Valorant', studio: 'Riot Games', clipCount: 47, hue: 350, color1: 'oklch(0.55 0.18 20)', color2: 'oklch(0.32 0.10 350)' },
  { id: 'apex',     name: 'Apex Legends', studio: 'Respawn',  clipCount: 32, hue: 25,  color1: 'oklch(0.58 0.16 35)', color2: 'oklch(0.30 0.08 25)' },
  { id: 'cs2',      name: 'Counter-Strike 2', studio: 'Valve', clipCount: 28, hue: 220, color1: 'oklch(0.50 0.14 240)', color2: 'oklch(0.30 0.08 220)' },
  { id: 'helldiv',  name: 'Helldivers 2', studio: 'Arrowhead', clipCount: 21, hue: 80,  color1: 'oklch(0.55 0.14 90)',  color2: 'oklch(0.32 0.08 70)' },
  { id: 'baldurs',  name: "Baldur's Gate 3", studio: 'Larian', clipCount: 14, hue: 50,  color1: 'oklch(0.60 0.13 60)',  color2: 'oklch(0.30 0.06 40)' },
  { id: 'rocket',   name: 'Rocket League', studio: 'Psyonix',  clipCount: 19, hue: 270, color1: 'oklch(0.55 0.16 280)', color2: 'oklch(0.30 0.10 270)' },
  { id: 'rs6',      name: 'Rainbow Six Siege', studio: 'Ubisoft', clipCount: 12, hue: 30, color1: 'oklch(0.50 0.14 30)', color2: 'oklch(0.28 0.06 20)' },
  { id: 'destiny',  name: 'Destiny 2', studio: 'Bungie', clipCount: 9, hue: 200, color1: 'oklch(0.55 0.13 200)', color2: 'oklch(0.30 0.08 220)' },
];

const TAGS = ['ace', 'clutch', 'fail', 'funny', '1v3', 'highlight', 'wholesome', 'glitch', 'speedrun', 'team-wipe', 'lucky', 'pentakill'];

// 24 sample clips. Use stable seeded numbers so layout is deterministic.
const CLIPS = [
  { id: 'c01', title: 'Ace on Haven retake', game: 'valorant', date: '2026-04-28', duration: 38, size: 142, tags: ['ace', 'clutch'], views: 12, collection: 'best-of-2026' },
  { id: 'c02', title: '1v4 with one bullet left', game: 'valorant', date: '2026-04-26', duration: 22, size: 88, tags: ['1v3', 'clutch'], views: 8, collection: 'best-of-2026' },
  { id: 'c03', title: 'Defuse with .3s left', game: 'cs2', date: '2026-04-24', duration: 18, size: 64, tags: ['clutch', 'highlight'], views: 23, collection: 'best-of-2026' },
  { id: 'c04', title: 'Wraith portal save', game: 'apex', date: '2026-04-22', duration: 31, size: 110, tags: ['clutch', 'team-wipe'], views: 5, collection: null },
  { id: 'c05', title: 'Octane R-99 spray', game: 'apex', date: '2026-04-20', duration: 14, size: 52, tags: ['highlight'], views: 3, collection: null },
  { id: 'c06', title: 'Bug Diver gone wrong', game: 'helldiv', date: '2026-04-19', duration: 47, size: 168, tags: ['fail', 'funny'], views: 17, collection: 'helldivers-fails' },
  { id: 'c07', title: 'Hellpod into a Charger', game: 'helldiv', date: '2026-04-17', duration: 12, size: 38, tags: ['fail', 'funny'], views: 22, collection: 'helldivers-fails' },
  { id: 'c08', title: 'Friendly fire moment', game: 'helldiv', date: '2026-04-15', duration: 28, size: 96, tags: ['fail', 'funny', 'wholesome'], views: 9, collection: 'helldivers-fails' },
  { id: 'c09', title: 'Aerial backboard goal', game: 'rocket', date: '2026-04-14', duration: 9,  size: 28, tags: ['highlight', 'lucky'], views: 41, collection: 'best-of-2026' },
  { id: 'c10', title: 'Half-flip resets', game: 'rocket', date: '2026-04-12', duration: 7,  size: 22, tags: ['highlight'], views: 6, collection: null },
  { id: 'c11', title: 'Karlach romance scene', game: 'baldurs', date: '2026-04-10', duration: 124, size: 412, tags: ['wholesome'], views: 14, collection: 'bg3-story' },
  { id: 'c12', title: 'Wild Magic backfire', game: 'baldurs', date: '2026-04-08', duration: 36, size: 128, tags: ['funny', 'fail'], views: 19, collection: 'bg3-story' },
  { id: 'c13', title: 'Kayo flash + ace', game: 'valorant', date: '2026-04-06', duration: 19, size: 72, tags: ['ace', 'highlight'], views: 11, collection: null },
  { id: 'c14', title: 'AWP through smoke', game: 'cs2', date: '2026-04-04', duration: 8,  size: 26, tags: ['highlight', 'lucky'], views: 7, collection: null },
  { id: 'c15', title: 'Mirage pistol round', game: 'cs2', date: '2026-04-02', duration: 53, size: 184, tags: ['clutch'], views: 4, collection: null },
  { id: 'c16', title: 'Sledge breach 1v3', game: 'rs6', date: '2026-03-30', duration: 24, size: 88, tags: ['1v3', 'clutch'], views: 10, collection: null },
  { id: 'c17', title: 'Mute jammer save', game: 'rs6', date: '2026-03-28', duration: 17, size: 60, tags: ['clutch'], views: 2, collection: null },
  { id: 'c18', title: 'Trials flawless run', game: 'destiny', date: '2026-03-26', duration: 312, size: 1024, tags: ['highlight', 'speedrun'], views: 29, collection: null },
  { id: 'c19', title: 'Raid jumping puzzle fail', game: 'destiny', date: '2026-03-24', duration: 21, size: 76, tags: ['fail', 'funny'], views: 13, collection: null },
  { id: 'c20', title: 'Pathfinder zip steal', game: 'apex', date: '2026-03-22', duration: 11, size: 38, tags: ['highlight', 'lucky'], views: 5, collection: null },
  { id: 'c21', title: 'Reyna clutch on Pearl', game: 'valorant', date: '2026-03-20', duration: 26, size: 92, tags: ['clutch', '1v3'], views: 16, collection: 'best-of-2026' },
  { id: 'c22', title: 'Bot wave clear glitch', game: 'helldiv', date: '2026-03-18', duration: 41, size: 148, tags: ['glitch', 'funny'], views: 8, collection: null },
  { id: 'c23', title: 'Solo queue ranked win', game: 'rocket', date: '2026-03-16', duration: 184, size: 612, tags: ['highlight'], views: 33, collection: null },
  { id: 'c24', title: 'Pistol round T-side', game: 'cs2', date: '2026-03-14', duration: 89, size: 312, tags: ['clutch', 'pentakill'], views: 6, collection: null },
];

const COLLECTIONS = [
  { id: 'best-of-2026', name: 'Best of 2026', shared: ['Mira', 'Jules', 'Theo'], cover: ['c01', 'c03', 'c09', 'c21'], note: 'Highlights I keep coming back to.' },
  { id: 'helldivers-fails', name: 'Helldivers Fails', shared: ['Mira'], cover: ['c06', 'c07', 'c08'], note: 'For democracy. Mostly.' },
  { id: 'bg3-story', name: 'BG3 — Act III', shared: [], cover: ['c11', 'c12'], note: 'Spoilers. Save for later.' },
  { id: 'watch-party-fri', name: 'Watch Party · Fri 8pm', shared: ['Mira', 'Jules', 'Theo', 'Ren'], cover: ['c02', 'c06', 'c11', 'c18', 'c21'], note: 'Bring snacks.' },
];

Object.assign(window, { GAMES, CLIPS, TAGS, COLLECTIONS });
