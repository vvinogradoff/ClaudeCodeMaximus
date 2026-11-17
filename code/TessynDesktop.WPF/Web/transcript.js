// Transcript page (variant A of the WebView2 prototype, plan 1.10): a virtualized list of pre-rendered entries.
// Security: all HTML in `html` is produced by the host with raw HTML disabled; headers are inserted as escaped text;
// links are never navigated by the page itself - a click is posted to the host, which validates the scheme and opens
// the system browser. This is the only channel to the host (no host objects exist).
'use strict';
const liveBox = document.getElementById('live');
const vp = document.getElementById('vp'), padTop = document.getElementById('padTop'), items = document.getElementById('items'), padBot = document.getElementById('padBot');
const BUF = 1.5;            // viewports of buffer above and below the visible area
const NEAR_TOP_PX = 300;    // scroll distance from the top that requests an older page
const BOTTOM_SLACK_PX = 60; // "at the bottom" tolerance for following appends
const ROLE = { U: 'USER', S: 'SYSTEM', C: 'COMPACTION' };   // the assistant has no role word

let S = null;               // { key, tok, items[], off[], a, b, hasOlder }
let olderPending = false;
let NG = PagingGate.create();   // newer-page request state (paging.js)
let rafPending = false;
let F = null;               // { term, idx[], cur }
let H = null;               // reference highlight: { i, quote } (entry jump from a Focus reference)
let ALLOW = [];             // hosts whose media previews load without a click (from the host; media-gate.js applies it)
let viewTimer = 0;
let liveSeen = { st: '', len: 0, queued: 0 };   // what the host was last told about the live row (diagnostics)
let liveTimer = 0;          // ticks the elapsed time of the live row once a second
let SEL = null;             // entry index of the selected input (kept by index across re-renders, appends, replaces and paging)
let SELAUTO = false;        // the walk mode: set by the click that selected an input (true = it was automated, so the arrows visit every input; false = only mine) and kept while the arrows move on, so a walk that starts on an automated input is not narrowed to mine by its first step. Kept beside SEL: the entry may not be loaded
let NAV = null;            // an Up/Down move waiting for a page to load: { dir, mineOnly, until }
const NAV_MARGIN_PX = 24;   // a revealed input sits this far below the top of the view
const NAV_WAIT_MS = 15000;  // a move still waiting for a page after this long is dropped (a failed page request must not fire later)

function post(o) { chrome.webview.postMessage(o); }
function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

function mk(e) { return { i: e.i, r: e.r, ts: e.ts, html: e.html, trunc: e.trunc, full: e.full, s: e.s || '', h: e.h || 60, m: e.m || [], p: e.p || [] }; }
function recomputeOff() { let y = 0; const n = S.items.length; S.off = new Float64Array(n + 1); for (let k = 0; k < n; k++) { S.off[k] = y; y += S.items[k].h; } S.off[n] = y; }
function findK(y) { let lo = 0, hi = S.items.length - 1; const off = S.off; while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (off[mid] <= y) lo = mid; else hi = mid - 1; } return lo; }
function posOfEntry(i) { return S.items.length ? Math.min(Math.max(i - S.items[0].i, 0), S.items.length - 1) : 0; }

function entryHtml(k) {
  const it = S.items[k];
  const more = it.trunc ? '<a class="more" data-i="' + it.i + '">Show full entry (' + it.full + ' chars)</a>' : '';
  const media = it.m.length ? MediaGate.previewHtml(it.m, ALLOW, it.i, S.loaded) : '';
  const cls = '<div class="e ' + esc(it.r) + (InputNav.isAuto(it) ? ' auto' : '') + '" data-k="' + k + '"><div class="c' + (it.i === SEL ? ' sel' : '') + '">';
  const tail = '<div class="b">' + it.html + '</div>' + more + media + chipsHtml(it) + '</div>';
  if (it.r === 'A' || it.r === 'S') {   // an assistant response sits on the view background: no role word, the model beside nothing, the time on the right (as in the Avalonia client)
    const cut = it.ts.indexOf(' · '), time = cut < 0 ? it.ts : it.ts.substring(0, cut), model = cut < 0 ? '' : it.ts.substring(cut + 3);
    return cls + (model ? '<div class="h">' + esc(model) + '</div>' : '') + tail + '<div class="t">' + esc(time) + '</div></div>';
  }
  if (it.r === 'U') {   // a user input: date over time in a column on the left, the bubble on the right; only an automated input has a label (SCHEDULED / SUPERVISOR)
    const cut = it.ts.indexOf(' · '), when = cut < 0 ? it.ts : it.ts.substring(0, cut), extra = cut < 0 ? '' : it.ts.substring(cut + 3);
    const sp = when.indexOf(' '), date = sp < 0 ? when : when.substring(0, sp), time = sp < 0 ? '' : when.substring(sp + 1);
    const label = InputNav.labelOf(it), head = label || extra ? '<div class="h">' + (label ? '<span class="lb">' + esc(label) + '</span>' : '') + (label && extra ? ' &middot; ' : '') + esc(extra) + '</div>' : '';
    return '<div class="e U' + (InputNav.isAuto(it) ? ' auto' : '') + '" data-k="' + k + '"><div class="ts"><div class="d">' + esc(date) + '</div><div class="tm">' + esc(time) + '</div></div><div class="c' + (it.i === SEL ? ' sel' : '') + '">' + head + tail + '</div>';
  }
  return cls + '<div class="h">' + esc(InputNav.labelOf(it) || ROLE[it.r] || it.r) + ' &middot; ' + esc(it.ts) + '</div>' + tail + '</div>';
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
  syncThumb();
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
  if (measure()) { recomputeOff(); padTop.style.height = S.off[S.a] + 'px'; padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; vp.scrollTop = S.off[anchorK] + d; syncThumb(); }
}
function atBottom() { return vp.scrollTop + vp.clientHeight >= vp.scrollHeight - BOTTOM_SLACK_PX; }

vp.addEventListener('scroll', () => {
  syncThumb();
  if (rafPending) return; rafPending = true;
  requestAnimationFrame(() => {
    rafPending = false; render(null); reportView(); PagingGate.onUserScroll(NG, vp.scrollTop);
    if (S && S.hasOlder && !olderPending && vp.scrollTop < NEAR_TOP_PX) { olderPending = true; post({ ev: 'needOlder', tok: S.tok }); }
    checkNewer();
  });
});
// A view that started at a jump target stops short of the newest entries: ask the host for the next page when the end is near.
function checkNewer() {
  if (S && PagingGate.shouldRequest(NG, S.hasNewer, vp.scrollTop, vp.clientHeight, vp.scrollHeight, NEAR_TOP_PX)) { PagingGate.markRequested(NG); post({ ev: 'needNewer', tok: S.tok }); }
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
  else onNavKey(e);
});

// ---- input selection and Up/Down navigation (inputnav.js) ----
// SEL is the selection even when that entry is not in the loaded window (SELAUTO remembers its kind); Up/Down then navigate from it.
function applySel() {
  items.querySelectorAll('.c.sel').forEach(c => c.classList.remove('sel'));
  if (SEL === null || !S) return;
  const k = posOfEntry(SEL); if (!S.items[k] || S.items[k].i !== SEL || k < S.a || k >= S.b) return;
  const c = items.children[k - S.a].querySelector('.c'); if (c) c.classList.add('sel');
}
function select(it) { SEL = it.i; SELAUTO = InputNav.isAuto(it); NAV = null; applySel(); }
// A click on an input selects it; a click anywhere else clears the selection. Links, chips and buttons keep their meaning (and leave the
// selection alone), and so does a drag that selected text.
function onSelectClick(e) {
  if (e.button !== 0 || !S) return;
  const t = e.target;
  if (t.closest && t.closest('a, button')) return;
  const sel = window.getSelection();
  if (sel && !sel.isCollapsed && vp.contains(sel.anchorNode)) return;
  const bubble = t.closest && t.closest('.c');   // the margin around a bubble is not the input
  const row = bubble && bubble.closest('.e');
  const it = row ? S.items[parseInt(row.getAttribute('data-k'), 10)] : null;
  if (it && InputNav.isInput(it)) select(it); else { SEL = null; NAV = null; applySel(); }
}
vp.addEventListener('click', onSelectClick);
function onNavKey(e) {
  const act = InputNav.keyAction(e.key, { ctrl: e.ctrlKey, alt: e.altKey, meta: e.metaKey, shift: e.shiftKey }, SEL !== null, vp.clientHeight);
  if (!act) return;
  e.preventDefault();
  NAV = null;
  if (act.type === 'scroll') vp.scrollTop += act.dy;
  else if (act.type === 'clear') { SEL = null; applySel(); }
  else if (S) stepNav(act.dir);
}
// Moves the selection to the nearest input in the loaded entries; when there is none and more entries can be loaded in that direction,
// asks the host for the next page and carries on once it arrives (resumeNav).
function stepNav(dir) {
  const p = InputNav.plan(S.items, SEL, SELAUTO, dir, S.hasOlder, S.hasNewer);
  if (p.type === 'reveal') { NAV = null; reveal(p.k); return; }
  if (p.type === 'none') { NAV = null; return; }
  NAV = { dir: dir, until: Date.now() + NAV_WAIT_MS };
  if (dir < 0) { if (!olderPending) { olderPending = true; post({ ev: 'needOlder', tok: S.tok }); } }
  else if (!NG.pending) { PagingGate.markRequested(NG); post({ ev: 'needNewer', tok: S.tok }); }
}
function resumeNav(pageEmpty) {
  if (!NAV || !S) return;
  if (!InputNav.shouldResume(NAV, Date.now(), SEL, pageEmpty)) { NAV = null; return; }
  stepNav(NAV.dir);
}
// Selects the input at position k and scrolls it to just below the top of the view. The entries above it may still have estimated heights,
// so the position is corrected once they are measured.
function reveal(k) {
  const i = S.items[k].i;
  SEL = i;   // SELAUTO stays: the walk mode is the one of the input the user clicked
  for (let pass = 0; pass < 3; pass++) {
    const want = Math.min(InputNav.revealTop(S.off, posOfEntry(i), NAV_MARGIN_PX), Math.max(0, vp.scrollHeight - vp.clientHeight));
    if (pass > 0 && Math.abs(vp.scrollTop - want) < 1) break;
    vp.scrollTop = want; render(null);
  }
  applySel(); reportView();
}

// ---- scrollbar (scrollmap.js) ----
// The native bar is hidden: its thumb is proportional to pixels, and unmeasured entries only have estimated heights, so dragging it moved the content by
// a varying amount. This thumb is proportional to the entry index; the drag maps the mouse to an entry (and the part of it) and anchors the view on that,
// so the content tracks the mouse and measuring other entries moves nothing.
const sb = document.getElementById('sb'), sbt = document.getElementById('sbt');
const SB = { drag: null, thumbLen: 0 };
function maxScroll() { return Math.max(0, vp.scrollHeight - vp.clientHeight); }
function trackPx() { return sb.clientHeight; }
function placeThumb(f, len) { const travel = Math.max(0, trackPx() - len); sbt.style.height = len + 'px'; sbt.style.transform = 'translateY(' + (f * travel) + 'px)'; }
function syncThumb() {
  if (!S || !S.items.length || maxScroll() <= 0) { sb.classList.add('off'); return; }
  sb.classList.remove('off');
  if (SB.drag) return;   // under the mouse the thumb is where the mouse put it, whatever the content does
  const len = ScrollMap.thumbLength(trackPx(), vp.clientHeight, S.off[S.items.length]);
  SB.thumbLen = len; placeThumb(ScrollMap.fractionOf(S.off, vp.scrollTop, maxScroll()), len);
}
function thumbFraction() { const travel = trackPx() - SB.thumbLen; return travel > 0 ? Math.min(1, Math.max(0, sbt.getBoundingClientRect().top - sb.getBoundingClientRect().top) / travel) : 0; }
function dragTo(y) {
  const d = SB.drag, travel = trackPx() - d.len;
  if (travel <= 0 || !S) return;
  const f = Math.min(1, Math.max(0, (d.startF * travel + (y - d.startY)) / travel));
  placeThumb(f, d.len);
  // Render the entries under the new position, then aim again: measuring them changed the heights the position was computed from (up to one entry's worth).
  for (let pass = 0; pass < 4; pass++) {
    const want = ScrollMap.scrollForFraction(S.off, f, maxScroll());
    if (pass > 0 && Math.abs(vp.scrollTop - want) < 1) break;
    vp.scrollTop = want; render(null);
  }
  d.lastY = y;
}
sbt.addEventListener('pointerdown', e => {
  if (e.button !== 0 || !S) return;
  e.preventDefault();
  sbt.setPointerCapture(e.pointerId);
  SB.drag = { startY: e.clientY, startF: thumbFraction(), len: SB.thumbLen, lastY: e.clientY, id: e.pointerId };
  sb.classList.add('drag');
});
sbt.addEventListener('pointermove', e => { if (SB.drag && e.pointerId === SB.drag.id) dragTo(e.clientY); });
function endDrag(e) { if (!SB.drag || e.pointerId !== SB.drag.id) return; SB.drag = null; sb.classList.remove('drag'); syncThumb(); }
sbt.addEventListener('pointerup', endDrag);
sbt.addEventListener('pointercancel', endDrag);
// Entries were added above the view (a page of older ones) while dragging: the content stays put, so the drag restarts from the thumb's new place.
function rebaseDrag() { if (SB.drag && S) { SB.drag.startY = SB.drag.lastY; SB.drag.startF = ScrollMap.fractionOf(S.off, vp.scrollTop, maxScroll()); placeThumb(SB.drag.startF, SB.drag.len); } }
// A click in the track outside the thumb pages toward it; the wheel over the bar scrolls as over the list.
sb.addEventListener('pointerdown', e => {
  if (e.target !== sb || e.button !== 0) return;
  e.preventDefault();
  vp.scrollTop += (e.clientY < sbt.getBoundingClientRect().top ? -1 : 1) * vp.clientHeight * 0.9;
});
sb.addEventListener('wheel', e => { e.preventDefault(); vp.scrollTop += e.deltaY; }, { passive: false });
new ResizeObserver(() => syncThumb()).observe(vp);

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
  showLive(null);
  SEL = InputNav.selectionOnLoad(SEL, S ? S.key : null, m.key);   // a reload of the same session keeps its selection; another session clears it
  S = { key: m.key, tok: m.tok, items: m.entries.map(mk), off: new Float64Array(1), a: 0, b: 0, hasOlder: m.hasOlder, hasNewer: !!m.hasNewer, loaded: new Map() };
  olderPending = false; NG = PagingGate.create(); F = null; H = null; NAV = null; recomputeOff(); items.replaceChildren();
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
  if (!m.entries.length) { resumeNav(true); return; }
  const k0 = findK(vp.scrollTop), anchorEntry = S.items[k0].i, anchorD = vp.scrollTop - S.off[k0];
  S.items = m.entries.map(mk).concat(S.items); recomputeOff(); S.a = S.b = 0;
  const k = posOfEntry(anchorEntry);
  vp.scrollTop = S.off[k] + anchorD; render(null);
  vp.scrollTop = S.off[posOfEntry(anchorEntry)] + anchorD;
  rebaseDrag();
  resumeNav();
}
function onAppend(m) {
  if (!S || m.tok !== S.tok) return;
  const follow = atBottom() && !S.hasNewer;   // a page loaded on demand (the view stops short of the end) must not drag the view down
  PagingGate.markLoaded(NG);
  S.hasNewer = !!m.hasNewer;
  for (const e of m.entries) S.items.push(mk(e));
  recomputeOff();
  if (follow) render('bottom'); else { padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; render(null); }
  rebaseDrag();
  checkNewer();
  resumeNav(m.entries.length === 0);
}
// ---- live row of a turn in flight (live.js) ----
// The row is created once and updated in place (so a selection in the streamed text survives); a one-second ticker keeps its timer.
const liveView = LiveView.create(document);
liveBox.appendChild(liveView.el);
function showLive(live) {
  const follow = atBottom();
  const shown = liveView.update(live, Date.now());
  if (shown && liveView.startAt != null) { if (!liveTimer) liveTimer = setInterval(() => { const t = liveBox.querySelector('.lt'); if (t) t.textContent = liveView.tickText(Date.now()); }, 1000); }
  else { clearInterval(liveTimer); liveTimer = 0; }
  if (follow) vp.scrollTop = vp.scrollHeight;
  reportLive(shown ? live : null);
}
// Tells the host what the page now shows (for its log and the end-to-end tests): on a change of status and after each further 1000 characters of text.
function reportLive(live) {
  const len = live && live.text ? live.text.length : 0, st = live ? (live.status || (live.error ? 'error' : '')) : '';
  const queued = live && live.queued ? live.queued.length : 0;
  if (len < liveSeen.len) liveSeen.len = 0;
  if (st === liveSeen.st && queued === liveSeen.queued && len < liveSeen.len + 1000) return;
  liveSeen = { st: st, len: len, queued: queued };
  post({ ev: 'liveShown', status: st, chars: len, tools: live && live.tools ? live.tools.length : 0, queued: queued });
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

// Theme and fonts from the host (W10a): the theme name and the three font sizes and the line spacing (--lh, FR.9.12) become attributes and CSS variables on the root; entry heights change with the font, so the list is measured again.
// The colours the user changed (Settings > Colours) are set as inline CSS variables over the page's own; the ones no longer listed are dropped again.
let colourVars = [];
function onColours(m) {
  const r = document.documentElement;
  const next = (m && typeof m.colours === 'object' && m.colours) ? Object.keys(m.colours).filter(k => /^--[a-z-]+$/.test(k) && /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/.test(m.colours[k])) : [];
  for (const k of colourVars) if (!next.includes(k)) r.style.removeProperty(k);
  for (const k of next) r.style.setProperty(k, m.colours[k]);
  colourVars = next;
}
function onAppearance(m) {
  const r = document.documentElement;
  onColours(m);
  if (m.theme === 'dark' || m.theme === 'light') r.setAttribute('data-theme', m.theme);
  const px = v => (typeof v === 'number' && v >= 6 && v <= 80) ? v + 'px' : null;
  let sizeChanged = false;
  const set = (name, v) => { const x = px(v); if (x && r.style.getPropertyValue(name) !== x) { r.style.setProperty(name, x); sizeChanged = true; } };
  set('--fs-a', m.asst); set('--fs-m', m.md); set('--fs-u', m.user);
  if (typeof m.lh === 'number' && m.lh >= 1 && m.lh <= 2.5) { const x = String(m.lh); if (r.style.getPropertyValue('--lh') !== x) { r.style.setProperty('--lh', x); sizeChanged = true; } }
  if (S && sizeChanged) remeasure();   // the entries change height only with a font size, not with the theme
}

// Ctrl+wheel over an entry changes the font size of what it shows (the host stores it); the browser's own zoom is off.
vp.addEventListener('wheel', e => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  const el = e.target.closest ? e.target.closest('.e, .lv') : null;
  const role = el && el.classList.contains('lv') ? 'L' : (el ? (['U', 'A', 'S', 'C'].find(c => el.classList.contains(c)) || 'A') : 'A');
  post({ ev: 'zoom', role, dir: e.deltaY < 0 ? 1 : -1 });
}, { passive: false });

chrome.webview.addEventListener('message', e => {
  const m = e.data; if (!m || typeof m.cmd !== 'string') return;
  switch (m.cmd) {
    case 'load': onLoad(m); break;
    case 'prepend': onPrepend(m); break;
    case 'append': onAppend(m); break;
    case 'replace': onReplace(m); break;
    case 'live': showLive(m.live); break;
    case 'find': if (S) doFind(m); break;
    case 'findStep': stepFind(m.dir); break;
    case 'clearFind': clearFind(); break;
    case 'goto': onGoto(m); break;
    case 'newerFailed': PagingGate.markFailed(NG, vp.scrollTop); NAV = null; break;
    case 'appearance': onAppearance(m); break;
    case 'policy': ALLOW = Array.isArray(m.hosts) ? m.hosts.filter(h => typeof h === 'string') : []; break;
    case 'mediaOk': case 'mediaFail': onMediaOk(m); break;
  }
});
// Pointer, wheel and key activity goes to the host (W10a screensaver): WPF never sees input that lands in a web page. Throttled; a pointer move counts after it travelled 6 px.
(function () {
  let last = 0, ax = null, ay = null;
  const act = () => { const n = Date.now(); if (n - last < 500) return; last = n; post({ ev: 'activity' }); };
  window.addEventListener('pointermove', e => {
    if (ax !== null && Math.hypot(e.screenX - ax, e.screenY - ay) < 6) return;
    const first = ax === null; ax = e.screenX; ay = e.screenY; if (!first) act();
  }, { passive: true, capture: true });
  for (const t of ['pointerdown', 'wheel', 'keydown']) window.addEventListener(t, act, { passive: true, capture: true });
})();
post({ ev: 'ready' });
