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
let rafPending = false;
let F = null;               // { term, idx[], cur }

function post(o) { chrome.webview.postMessage(o); }
function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

function mk(e) { return { i: e.i, r: e.r, ts: e.ts, html: e.html, trunc: e.trunc, full: e.full, h: e.h || 60 }; }
function recomputeOff() { let y = 0; const n = S.items.length; S.off = new Float64Array(n + 1); for (let k = 0; k < n; k++) { S.off[k] = y; y += S.items[k].h; } S.off[n] = y; }
function findK(y) { let lo = 0, hi = S.items.length - 1; const off = S.off; while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (off[mid] <= y) lo = mid; else hi = mid - 1; } return lo; }
function posOfEntry(i) { return S.items.length ? Math.min(Math.max(i - S.items[0].i, 0), S.items.length - 1) : 0; }

function entryHtml(k) {
  const it = S.items[k];
  const more = it.trunc ? '<a class="more" data-i="' + it.i + '">Show full entry (' + it.full + ' chars)</a>' : '';
  return '<div class="e ' + esc(it.r) + '" data-k="' + k + '"><div class="c"><div class="h">' + esc(ROLE[it.r] || it.r) + ' &middot; ' + esc(it.ts) + '</div><div class="b">' + it.html + '</div>' + more + '</div></div>';
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
}
function atBottom() { return vp.scrollTop + vp.clientHeight >= vp.scrollHeight - BOTTOM_SLACK_PX; }

vp.addEventListener('scroll', () => {
  if (rafPending) return; rafPending = true;
  requestAnimationFrame(() => {
    rafPending = false; render(null);
    if (S && S.hasOlder && !olderPending && vp.scrollTop < NEAR_TOP_PX) { olderPending = true; post({ ev: 'needOlder', tok: S.tok }); }
  });
});

// Links: never navigate in the page; post the click to the host.
function onLinkClick(e) {
  const more = e.target.closest && e.target.closest('a.more');
  if (more) { e.preventDefault(); post({ ev: 'expand', tok: S ? S.tok : 0, i: parseInt(more.getAttribute('data-i'), 10) }); return; }
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
  S = { key: m.key, tok: m.tok, items: m.entries.map(mk), off: new Float64Array(1), a: 0, b: 0, hasOlder: m.hasOlder };
  olderPending = false; F = null; recomputeOff(); items.replaceChildren();
  padTop.style.height = '0px'; padBot.style.height = S.off[S.items.length] + 'px';
  const t0 = performance.now();
  vp.scrollTop = m.toBottom ? S.off[S.items.length] : 0;
  render(m.toBottom ? 'bottom' : 'top');
  const tok = m.tok, key = m.key, js = performance.now() - t0;
  requestAnimationFrame(() => post({ ev: 'painted', tok: tok, key: key, jsMs: js, dom: document.getElementsByTagName('*').length }));
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
  const follow = atBottom();
  for (const e of m.entries) S.items.push(mk(e));
  recomputeOff();
  if (follow) render('bottom'); else { padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; render(null); }
}
function onReplace(m) {
  if (!S || m.tok !== S.tok) return;
  const k = posOfEntry(m.entry.i); if (S.items[k].i !== m.entry.i) return;
  S.items[k] = mk(m.entry);
  if (k >= S.a && k < S.b) { const el = items.children[k - S.a]; const f = fragmentOf(k, k + 1); el.replaceWith(f); if (F) hlRange(items); }
  const anchorK = findK(vp.scrollTop), d = vp.scrollTop - S.off[anchorK];
  if (measure()) { recomputeOff(); padTop.style.height = S.off[S.a] + 'px'; padBot.style.height = (S.off[S.items.length] - S.off[S.b]) + 'px'; vp.scrollTop = S.off[anchorK] + d; }
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
  }
});
post({ ev: 'ready' });
