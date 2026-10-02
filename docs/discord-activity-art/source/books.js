// Shared drawing helpers for the Activity art: banded books on shelves, in the favicon's style.
const NS = "http://www.w3.org/2000/svg";
const BAND = "#d6ecd8";
const SHELF = "#f6f5ef";
// Brand greens plus a few muted cloth colours like the library shelf's game books.
const CLOTHS = ["#317f54", "#97cca8", "#2a6b47", "#5fa57a", "#3f8f62", "#b9a36a", "#8a5a44", "#4f6f86", "#7f9c6e", "#245a3d"];

function rng(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

function el(name, attrs, parent) {
  const node = document.createElementNS(NS, name);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  if (parent) parent.appendChild(node);
  return node;
}

// One book standing on y=0 (grows upward), origin at its bottom-left.
function book(parent, { x, w, h, cloth, scale = 1, lean = 0, play = false }) {
  const g = el("g", { transform: `translate(${x} 0) rotate(${lean} ${w} 0)` }, parent);
  const r = 5 * scale;
  const band = 3.2 * scale;
  el("rect", { x: 0, y: -h, width: w, height: h, rx: r, fill: cloth }, g);
  const top = -h + 0.12 * h;
  el("rect", { x: 0, y: top, width: w, height: band, fill: BAND, opacity: 0.9 }, g);
  el("rect", { x: 0, y: top + band * 3.5, width: w, height: band, fill: BAND, opacity: 0.9 }, g);
  el("rect", { x: 0, y: -0.1 * h - band, width: w, height: band, fill: BAND, opacity: 0.9 }, g);
  if (play) {
    const cx = w / 2, cy = -h * 0.52, s = Math.min(w * 0.28, 14 * scale);
    el("path", { d: `M${cx - s * 0.6} ${cy - s} L${cx + s} ${cy} L${cx - s * 0.6} ${cy + s} Z`, fill: BAND }, g);
  }
  return w;
}

// A run of books from x0 to x1 standing on a plank at baseline y.
function shelf(svg, { x0, x1, y, seed, scale = 1, minH = 70, maxH = 120, gapChance = 0.12, leanLast = true, plank = true, playAt = -1 }) {
  const rand = rng(seed);
  const g = el("g", { transform: `translate(0 ${y})` }, svg);
  let x = x0;
  let i = 0;
  const books = [];
  while (x < x1) {
    const w = (16 + rand() * 16) * scale;
    if (x + w > x1) break;
    if (i > 0 && rand() < gapChance) {
      x += (6 + rand() * 18) * scale;
      i++;
      continue;
    }
    const h = (minH + rand() * (maxH - minH)) * scale;
    const cloth = CLOTHS[Math.floor(rand() * CLOTHS.length)];
    books.push({ x, w, h, cloth });
    x += w + 3 * scale;
    i++;
  }
  if (leanLast && books.length > 2) {
    // Like the logo: the end book leans back onto its neighbour, pivoting on its bottom-right corner.
    const last = books[books.length - 1];
    const prev = books[books.length - 2];
    last.lean = -11.3;
    last.h = Math.min(last.h, prev.h * 0.95);
    last.x = prev.x + prev.w + Math.sin(11.3 * Math.PI / 180) * last.h + 2 * scale;
    if (last.x + last.w > x1 + 10 * scale) books.pop();
  }
  books.forEach((b, idx) => book(g, { ...b, scale, play: idx === playAt }));
  if (plank) {
    el("rect", { x: x0 - 14 * scale, y: 2 * scale, width: x1 - x0 + 28 * scale, height: 12 * scale, rx: 6 * scale, fill: SHELF }, g);
  }
  return g;
}
