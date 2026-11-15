// Transcript page (variant A of the WebView2 prototype, plan 1.10): a virtualized list of pre-rendered entries.
// Security: all HTML in `html` is produced by the host with raw HTML disabled; headers are inserted as escaped text;
// links are never navigated by the page itself - a click is posted to the host, which validates the scheme and opens
// the system browser. This is the only channel to the host (no host objects exist).
'use strict';
const vp = document.getElementById('vp'), padTop = document.getElementById('padTop'), items = document.getElementById('items'), padBot = document.getElementById('padBot');
const BUF = 1.5;            // viewports of buffer above and below the visible area
const NEAR_TOP_PX = 300;    // scroll distance from the top that requests an older page
const BOTTOM_SLACK_PX = 60; // "at the bottom" tolerance for following appends
const ROLE = { U: 'USER', A: 'ASSISTANT', S: 'SYSTEM', C: 'COMPACTION' };

let S = null;               // { key, tok, items[], off[], a, b, hasOlder }
let olderPending = false;
let newerPending = false;
let rafPending = false;
let F = null;               // { term, idx[], cur }
let H = null;               // reference highlight: { i, quote } (entry jump from a Focus reference)
let ALLOW = [];             // hosts whose media previews load without a click (from the host; media-gate.js applies it)
let viewTimer = 0;

function post(o) { chrome.webview.postMessage(o); }
function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

function mk(e) { return { i: e.i, r: e.r, ts: e.ts, html: e.html, trunc: e.trunc, full: e.full, h: e.h || 60, m: e.m || [], p: e.p || [] }; }
function recomputeOff() { let y = 0; const n = S.items.length; S.off = new Float64Array(n + 1); for (let k = 0; k < n; k++) { S.off[k] = y; y += S.items[k].h; } S.off[n] = y; }
function findK(y) { let lo = 0, hi = S.items.length - 1; const off = S.off; while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (off[mid] <= y) lo = mid; else hi = mid - 1; } return lo; }
function posOfEntry(i) { return S.items.length ? Math.min(Math.max(i - S.items[0].i, 0), S.items.length - 1) : 0; }

function entryHtml(k) {
  const it = S.items[k];
  const more = it.trunc ? '<a class="more" data-i="' + it.i + '">Show full entry (' + it.full + ' chars)</a>' : '';
  const media = it.m.length ? MediaGate.previewHtml(it.m, ALLOW, it.i, S.loaded) : '';
  return '<div class="e ' + esc(it.r) + '" data-k="' + k + '"><div class="c"><div class="h">' + esc(ROLE[it.r] || it.r) + ' &middot; ' + esc(it.ts) + '</div><div class="b">' + it.html + '</div>' + more + media + chipsHtml(it) + '</div></div>';
}
// Local-file chips (FR.26.5): the path is kept in S.items, the DOM carries only its index and the escaped file name.
function chipsHtml(it) {
  if (!it.p.length) return '';
  let s = '<div class="chips">';
  it.p.forEach((path, j) => { s += '<button class="chip" type="button" data-i="' + it.i + '" data-j="' + j + '" title="' + esc(path) + '">' + esc(path.substring(path.lastIndexOf('\\') + 1)) + '</button>'; });
  return s + '</div>';
}
function rangeHtml(a, b) { let s = ''; for (let k = a; k < b; k++) s += entryHtml(k); return s; }
function fragmentOf(a, b) { const t = document.createElement('div'); t.innerHTML = rangeHtml(a, b); const f = document.createDocumentFragment(); while (t.firstChild) f.appendChild(t.firstChild); return f; }

function syncDom(a, b) {
  if (S.b <= S.a || b <= S.a || a >= S.b) { items.replaceChildren(fragmentOf(a, b)); S.a = a; S.b = b; if (F) hlRange(items); return; }
  while (S.a < a) { items.removeChild(items.firstChild); S.a++; }
  while (S.b > b) { items.removeChild(items.lastChild); S.b--; }
  if (a < S.a) { const f = fragmentOf(a, S.a); if (F) hlRange(f); items.insertBefore(f, items.firstChild); S.a = a; }
  if (b > S.b) { const f = fragmentOf(S.b, b); if (F) hlRange(f); items.appendChild(f); S.b = b; }
}
function covered() {
  const top = vp.scrollTop, vh = vp.clientHeight, n = S.items.length;
  if (S.b <= S.a) return false;
  return (S.a === 0 || S.off[S.a] <= top - 0.5 * vh) && (S.b === n || S.off[S.b] >= top + vh + 0.5 * vh);
}
function measure() {
  const kids = items.children; let changed = false;
  for (let j = 0; j < kids.length; j++) { const h = kids[j].offsetHeight, k = S.a + j; if (Math.abs(S.items[k].h - h) > 0.5) { S.items[k].h = h; changed = true; } }
  return changed;
}
function render(pin) {
  if (!S || !S.items.length) { items.replaceChildren(); padTop.style.height = '0px'; padBot.style.height = '0px'; if (S) S.a = S.b = 0; return; }
  if (!pin && covered()) return;
  const vh = vp.clientHeight, n = S.items.length;
  let anchorK = -1, anchorD = 0;
  if (!pin) { anchorK = findK(vp.scrollTop); anchorD = vp.scrollTop - S.off[anchorK]; }
  for (let pass = 0; pass < 3; pass++) {
    const top = vp.scrollTop;
    const a = findK(Math.max(0, top - BUF * vh));
    let b = findK(Math.min(top + vh + BUF * vh, Math.max(0, S.off[n] - 1))) + 1; if (b > n) b = n;
    syncDom(a, b);
    const changed = measure();
    if (changed) recomputeOff();
    padTop.style.height = S.off[S.a] + 'px'; padBot.style.height = (S.off[n] - S.off[S.b]) + 'px';
    if (pin === 'bottom') vp.scrollTop = vp.scrollHeight; else if (pin === 'top') vp.scrollTop = 0; else if (anchorK >= 0) vp.scrollTop = S.off[anchorK] + anchorD;
    if (!changed) break;
  }
  if (H) applyRefHl();
}
// Reports the entry at the top of the view (debounced), for the host's Back stack.
function reportView() {
  clearTimeout(viewTimer);
  viewTimer = setTimeout(() => { if (S && S.items.length) post({ ev: 'view', tok: S.tok, i: S.items[findK(vp.scrollTop)].i, bottom: atBottom() }); }, 150);
}
// Re-measures after an image or video sized itself; the entry at the top of the view stays where it is.
function remeasure() {
  if (!S || !S.items.length) return;
  const anchorK = findK(vp.scrollTop), d = vp.scrollTop - S.off[anchorK];
  if (measure()) { recomputeOff(); padTop.style.height = S.off[S.a] + 'px'; padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; vp.scrollTop = S.off[anchorK] + d; }
}
function atBottom() { return vp.scrollTop + vp.clientHeight >= vp.scrollHeight - BOTTOM_SLACK_PX; }

vp.addEventListener('scroll', () => {
  if (rafPending) return; rafPending = true;
  requestAnimationFrame(() => {
    rafPending = false; render(null); reportView();
    if (S && S.hasOlder && !olderPending && vp.scrollTop < NEAR_TOP_PX) { olderPending = true; post({ ev: 'needOlder', tok: S.tok }); }
    checkNewer();
  });
});
// A view that started at a jump target stops short of the newest entries: ask the host for the next page when the end is near.
function checkNewer() {
  if (S && S.hasNewer && !newerPending && vp.scrollTop + vp.clientHeight >= vp.scrollHeight - NEAR_TOP_PX) { newerPending = true; post({ ev: 'needNewer', tok: S.tok }); }
}
// Media size themselves after they load; a failed preview becomes a short note (no retry, no remote content left behind).
items.addEventListener('load', e => { if (e.target.tagName === 'IMG') requestAnimationFrame(remeasure); }, true);
items.addEventListener('loadedmetadata', e => { if (e.target.tagName === 'VIDEO') requestAnimationFrame(remeasure); }, true);
items.addEventListener('error', e => {
  const t = e.target; if (!t || (t.tagName !== 'IMG' && t.tagName !== 'VIDEO')) return;
  const note = document.createElement('span'); note.className = 'mfail'; note.textContent = 'Preview unavailable'; t.replaceWith(note); requestAnimationFrame(remeasure);
}, true);

function entryOf(i) { if (!S) return null; const n = parseInt(i, 10), k = posOfEntry(n); return S.items[k] && S.items[k].i === n ? S.items[k] : null; }

// Links: never navigate in the page; post the click to the host.
function onLinkClick(e) {
  const more = e.target.closest && e.target.closest('a.more');
  if (more) { e.preventDefault(); post({ ev: 'expand', tok: S ? S.tok : 0, i: parseInt(more.getAttribute('data-i'), 10) }); return; }
  const chip = e.target.closest && e.target.closest('button.chip');
  if (chip) { const it = entryOf(chip.getAttribute('data-i')); const path = it && it.p[parseInt(chip.getAttribute('data-j'), 10)]; if (path) post({ ev: 'path', tok: S.tok, i: it.i, path: path }); return; }
  const load = e.target.closest && e.target.closest('button.mload');
  if (load) {
    // Click-to-load: ask the host first; only its "mediaOk" lets this page create the element (the URL is not in the DOM until then).
    const it = entryOf(load.getAttribute('data-i')), j = parseInt(load.getAttribute('data-j'), 10);
    const p = it && MediaGate.plan(it.m, ALLOW)[j];
    if (p && !p.auto) post({ ev: 'mediaLoad', tok: S.tok, i: it.i, j: j, url: p.url });
    return;
  }
  const a = e.target.closest && e.target.closest('a');
  if (!a) return;
  e.preventDefault();
  if (a.classList.contains('blocked')) return;
  const href = a.getAttribute('href') || '';
  if (/^https?:\/\//i.test(href)) post({ ev: 'link', href: href });
}
vp.addEventListener('click', onLinkClick);
vp.addEventListener('auxclick', onLinkClick);
document.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && (e.key === 'f' || e.key === 'F')) { e.preventDefault(); post({ ev: 'findRequested' }); }
  else if (e.key === 'F3') { e.preventDefault(); post({ ev: 'findStep', dir: e.shiftKey ? -1 : 1 }); }
});

// ---- find ----
function hlRange(root) {
  if (!F || !F.term) return;
  const t = F.term.toLowerCase(), len = t.length;
  const bodies = root.classList && root.classList.contains('b') ? [root] : root.querySelectorAll('.b');
  bodies.forEach(b => {
    if (b.dataset.hl === F.term) return; b.dataset.hl = F.term;
    const w = document.createTreeWalker(b, NodeFilter.SHOW_TEXT); const nodes = []; let n; while ((n = w.nextNode())) nodes.push(n);
    for (const nd of nodes) {
      const s = nd.nodeValue, low = s.toLowerCase(); let i = low.indexOf(t); if (i < 0) continue;
      const frag = document.createDocumentFragment(); let pos = 0;
      while (i >= 0) { if (i > pos) frag.appendChild(document.createTextNode(s.substring(pos, i))); const m = document.createElement('mark'); m.textContent = s.substr(i, len); frag.appendChild(m); pos = i + len; i = low.indexOf(t, pos); }
      if (pos < s.length) frag.appendChild(document.createTextNode(s.substring(pos)));
      nd.parentNode.replaceChild(frag, nd);
    }
  });
}
function clearFind() { F = null; items.querySelectorAll('.b').forEach(b => delete b.dataset.hl); if (S) { S.a = S.b = 0; render(null); } }
function gotoMatch() {
  if (!F || !F.idx.length || !S.items.length) return;
  const entry = F.idx[F.cur]; const k = posOfEntry(entry);
  vp.scrollTop = Math.max(0, S.off[k] - vp.clientHeight / 3); render(null);
  items.querySelectorAll('mark.cur').forEach(m => m.classList.remove('cur'));
  const el = items.querySelector('.e[data-k="' + k + '"] mark'); if (el) { el.classList.add('cur'); el.scrollIntoView({ block: 'center' }); }
  post({ ev: 'findStatus', cur: F.cur + 1, count: F.idx.length });
}
function doFind(m) {
  F = { term: m.term, idx: m.idx.slice(), cur: 0 };
  items.querySelectorAll('.b').forEach(b => delete b.dataset.hl);
  if (!F.idx.length) { post({ ev: 'findStatus', cur: 0, count: 0 }); S.a = S.b = 0; render(null); return; }
  F.cur = F.idx.length - 1;                // start at the newest match
  S.a = S.b = 0; gotoMatch();
}
function stepFind(dir) { if (!F || !F.idx.length) return; F.cur = (F.cur + dir + F.idx.length) % F.idx.length; gotoMatch(); }

// ---- host commands ----
function onLoad(m) {
  S = { key: m.key, tok: m.tok, items: m.entries.map(mk), off: new Float64Array(1), a: 0, b: 0, hasOlder: m.hasOlder, hasNewer: !!m.hasNewer, loaded: new Map() };
  olderPending = false; newerPending = false; F = null; H = null; recomputeOff(); items.replaceChildren();
  padTop.style.height = '0px'; padBot.style.height = S.off[S.items.length] + 'px';
  const t0 = performance.now();
  vp.scrollTop = m.toBottom ? S.off[S.items.length] : 0;
  render(m.toBottom ? 'bottom' : 'top');
  const tok = m.tok, key = m.key, js = performance.now() - t0;
  requestAnimationFrame(() => { post({ ev: 'painted', tok: tok, key: key, jsMs: js, dom: document.getElementsByTagName('*').length }); checkNewer(); });
}
function onPrepend(m) {
  olderPending = false;
  if (!S || m.tok !== S.tok) return;
  S.hasOlder = m.hasOlder;
  if (!m.entries.length) return;
  const k0 = findK(vp.scrollTop), anchorEntry = S.items[k0].i, anchorD = vp.scrollTop - S.off[k0];
  S.items = m.entries.map(mk).concat(S.items); recomputeOff(); S.a = S.b = 0;
  const k = posOfEntry(anchorEntry);
  vp.scrollTop = S.off[k] + anchorD; render(null);
  vp.scrollTop = S.off[posOfEntry(anchorEntry)] + anchorD;
}
function onAppend(m) {
  if (!S || m.tok !== S.tok) return;
  const follow = atBottom() && !S.hasNewer;   // a page loaded on demand (the view stops short of the end) must not drag the view down
  newerPending = false;
  S.hasNewer = !!m.hasNewer;
  for (const e of m.entries) S.items.push(mk(e));
  recomputeOff();
  if (follow) render('bottom'); else { padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; render(null); }
  checkNewer();
}
function onReplace(m) {
  if (!S || m.tok !== S.tok) return;
  const k = posOfEntry(m.entry.i); if (S.items[k].i !== m.entry.i) return;
  S.items[k] = mk(m.entry);
  if (k >= S.a && k < S.b) { const el = items.children[k - S.a]; const f = fragmentOf(k, k + 1); el.replaceWith(f); if (F) hlRange(items); }
  const anchorK = findK(vp.scrollTop), d = vp.scrollTop - S.off[anchorK];
  if (measure()) { recomputeOff(); padTop.style.height = S.off[S.a] + 'px'; padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; vp.scrollTop = S.off[anchorK] + d; }
}
// ---- reference jump (FR.23.18): scroll an entry into view and highlight the quoted text ----
// The quote is plain text from the entry's source; the page shows rendered markdown, so both sides are compared with whitespace
// collapsed and the emphasis characters * _ ` ignored. When the quote cannot be located the whole entry is outlined instead.
const SKIP_CHARS = '*_`';
function normalizeQuote(q) { let s = '', sp = true; for (const c of String(q)) { if (/\s/.test(c)) { if (!sp) { s += ' '; sp = true; } } else if (SKIP_CHARS.indexOf(c) < 0) { s += c; sp = false; } } return s.replace(/ $/, ''); }
function textMap(root) {
  const w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT); const map = []; let s = '', sp = true, n;
  while ((n = w.nextNode())) {
    const v = n.nodeValue;
    for (let o = 0; o < v.length; o++) {
      const c = v[o];
      if (/\s/.test(c)) { if (!sp) { s += ' '; map.push([n, o]); sp = true; } }
      else if (SKIP_CHARS.indexOf(c) < 0) { s += c; map.push([n, o]); sp = false; }
    }
  }
  return { s, map };
}
function applyRefHl() {
  if (!H) return;
  const k = posOfEntry(H.i); if (!S.items[k] || S.items[k].i !== H.i || k < S.a || k >= S.b) return;
  const el = items.children[k - S.a]; if (!el || el.querySelector('mark.ref, .c.refhl')) return;
  const q = H.quote ? normalizeQuote(H.quote) : '';
  const body = el.querySelector('.b'); let hit = false;
  if (q && body) {
    const t = textMap(body), at = t.s.indexOf(q);
    if (at >= 0) {
      const segs = new Map();
      for (let x = at; x < at + q.length; x++) { const [node, off] = t.map[x]; const g = segs.get(node); if (g) g[1] = off + 1; else segs.set(node, [off, off + 1]); }
      for (const [node, [from, to]] of Array.from(segs.entries()).reverse()) { const r = document.createRange(); r.setStart(node, from); r.setEnd(node, to); const m = document.createElement('mark'); m.className = 'ref'; r.surroundContents(m); }
      hit = true;
    }
  }
  if (!hit) { const c = el.querySelector('.c'); if (c) c.classList.add('refhl'); }
}
function onGoto(m) {
  if (!S || !S.items.length) return;
  const k = posOfEntry(m.i); if (S.items[k].i !== m.i) return;
  H = m.hl === false ? null : { i: m.i, quote: m.quote || null };
  for (let pass = 0; pass < 2; pass++) { vp.scrollTop = Math.max(0, S.off[posOfEntry(m.i)] - vp.clientHeight / 4); S.a = S.b = 0; render(null); }
  applyRefHl(); reportView();
  // Diagnostics for the host log (and the end-to-end test): was the quote found, and was the entry on screen?
  const k2 = posOfEntry(m.i), vis = k2 >= S.a && k2 < S.b && S.off[k2] < vp.scrollTop + vp.clientHeight && S.off[k2 + 1] > vp.scrollTop;
  post({ ev: 'refHl', tok: S.tok, i: m.i, hl: m.hl !== false, quote: !!items.querySelector('mark.ref'), entry: !!items.querySelector('.c.refhl'), visible: vis });
}
function onMediaOk(m) {
  if (!S || m.tok !== S.tok) return;
  if (typeof m.url !== 'string') return;
  S.loaded.set(m.url, m.cmd === 'mediaFail' ? null : (typeof m.src === 'string' ? m.src : ''));
  const k = posOfEntry(m.i); if (!S.items[k] || S.items[k].i !== m.i) return;
  if (k >= S.a && k < S.b) { const el = items.children[k - S.a]; el.replaceWith(fragmentOf(k, k + 1)); if (F) hlRange(items); applyRefHl(); }
  remeasure();
}

chrome.webview.addEventListener('message', e => {
  const m = e.data; if (!m || typeof m.cmd !== 'string') return;
  switch (m.cmd) {
    case 'load': onLoad(m); break;
    case 'prepend': onPrepend(m); break;
    case 'append': onAppend(m); break;
    case 'replace': onReplace(m); break;
    case 'find': if (S) doFind(m); break;
    case 'findStep': stepFind(m.dir); break;
    case 'clearFind': clearFind(); break;
    case 'goto': onGoto(m); break;
    case 'policy': ALLOW = Array.isArray(m.hosts) ? m.hosts.filter(h => typeof h === 'string') : []; break;
    case 'mediaOk': case 'mediaFail': onMediaOk(m); break;
  }
});
post({ ev: 'ready' });
