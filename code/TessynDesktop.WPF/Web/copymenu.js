// Copy as Markdown in the transcript page (FR.2.13). Loaded after transcript.js and part of the same page: it uses the page's state (S, items, vp, ALLOW, post).
// A right-click on selected text opens a small menu (Copy, Copy as Markdown); Ctrl+Shift+C is Copy as Markdown. The menu is part of the page, so it uses the
// page's theme variables (the user's colours too) and a test can reach it through the DevTools port. The clipboard is the host's: the page posts what to copy.
// A selection that covers whole entries is answered by the host from their original Markdown (it keeps it per entry); everything else is converted here
// (mdcopy.js).
'use strict';
const COPY_SLOP_PX = 4;      // a right-click this close to the selection's boxes counts as on the selection
const CM_MARGIN_PX = 4;      // the menu keeps this distance from the edges of the page
const cm = document.createElement('div');
cm.className = 'cm'; cm.hidden = true; cm.setAttribute('role', 'menu');
cm.innerHTML = '<button type="button" role="menuitem" data-act="text">Copy<span class="k">Ctrl+C</span></button><button type="button" role="menuitem" data-act="markdown">Copy as Markdown<span class="k">Ctrl+Shift+C</span></button>';
document.body.appendChild(cm);

function menuOpen() { return !cm.hidden; }
function hideMenu() { cm.hidden = true; cm.querySelectorAll('.on').forEach(b => b.classList.remove('on')); }

// The selection as the page shows it, or null: collapsed, outside the transcript, or no transcript.
function transcriptSelection() {
  const sel = window.getSelection();
  if (!S || !sel || sel.isCollapsed || !sel.rangeCount || !vp.contains(sel.anchorNode) || !vp.contains(sel.focusNode)) return null;
  return sel;
}
function onSelection(sel, x, y) {
  for (let r = 0; r < sel.rangeCount; r++)
    for (const b of sel.getRangeAt(r).getClientRects()) if (x >= b.left - COPY_SLOP_PX && x <= b.right + COPY_SLOP_PX && y >= b.top - COPY_SLOP_PX && y <= b.bottom + COPY_SLOP_PX) return true;
  return false;
}
function showMenu(x, y) {
  cm.hidden = false;
  cm.style.left = Math.max(CM_MARGIN_PX, Math.min(x, window.innerWidth - cm.offsetWidth - CM_MARGIN_PX)) + 'px';
  cm.style.top = Math.max(CM_MARGIN_PX, Math.min(y, window.innerHeight - cm.offsetHeight - CM_MARGIN_PX)) + 'px';
}

document.addEventListener('contextmenu', e => {
  e.preventDefault();   // the browser's own menu is off in the host; the page shows only this one
  hideMenu();
  if (!S || !vp.contains(e.target) || (e.target.closest && e.target.closest('a, .pth, img.pimg'))) return;   // a link or a path keeps its own behaviour
  const sel = transcriptSelection();
  if (sel && onSelection(sel, e.clientX, e.clientY)) showMenu(e.clientX, e.clientY);
}, true);
cm.addEventListener('mousedown', e => e.preventDefault());   // pressing a menu item keeps the selection and the focus where they are
cm.addEventListener('click', e => { const b = e.target.closest && e.target.closest('button[data-act]'); if (b) { hideMenu(); copySelection(b.getAttribute('data-act')); } });
document.addEventListener('mousedown', e => { if (menuOpen() && !cm.contains(e.target)) hideMenu(); }, true);
vp.addEventListener('wheel', hideMenu, { passive: true });
vp.addEventListener('scroll', hideMenu, { passive: true });
window.addEventListener('blur', hideMenu);
window.addEventListener('resize', hideMenu);

// Keys (capture phase, before the page's own handler): Ctrl+Shift+C copies as Markdown; while the menu is open Up/Down move the highlight, Enter chooses and
// Esc closes (the focus stays in the transcript, so the menu cannot have it).
document.addEventListener('keydown', e => {
  if (menuOpen()) {
    const btns = Array.from(cm.querySelectorAll('button')), at = btns.findIndex(b => b.classList.contains('on'));
    if (e.key === 'Escape') hideMenu();
    else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { const n = (at + (e.key === 'ArrowDown' ? 1 : -1) + btns.length) % btns.length; btns.forEach((b, k) => b.classList.toggle('on', k === n)); }
    else if (e.key === 'Enter' && at >= 0) { const act = btns[at].getAttribute('data-act'); hideMenu(); copySelection(act); }
    else return;
    e.preventDefault(); e.stopImmediatePropagation();
    return;
  }
  if (e.ctrlKey && e.shiftKey && !e.altKey && !e.metaKey && (e.key === 'c' || e.key === 'C') && copySelection('markdown')) { e.preventDefault(); e.stopImmediatePropagation(); }
}, true);

// The part of a range inside el (the range itself when it lies inside).
function clampRange(range, el) {
  const box = document.createRange(); box.selectNodeContents(el);
  const sub = range.cloneRange();
  if (sub.compareBoundaryPoints(Range.START_TO_START, box) < 0) sub.setStart(box.startContainer, box.startOffset);
  if (sub.compareBoundaryPoints(Range.END_TO_END, box) > 0) sub.setEnd(box.endContainer, box.endOffset);
  return sub;
}
// True when no text of el lies outside the range (a drag over a whole entry, a triple click, Ctrl+A).
function coversText(range, el) {
  if (range.comparePoint(el, 0) < 0) { const before = document.createRange(); before.setStart(el, 0); before.setEnd(range.startContainer, range.startOffset); if (before.toString().trim()) return false; }
  if (range.comparePoint(el, el.childNodes.length) > 0) { const after = document.createRange(); after.setStart(range.endContainer, range.endOffset); after.setEnd(el, el.childNodes.length); if (after.toString().trim()) return false; }
  return true;
}
// What an image or a video preview of an entry stands for (the DOM shows only a cache URL, or no address at all before the click): its path or its link.
function mediaOf(el, it) {
  const j = parseInt(el.getAttribute('data-j'), 10);
  if (isNaN(j)) return null;
  if (el.classList.contains('pimg')) { const path = it.p[j]; return path ? { url: path, alt: PathSlots.leaf(path), video: PathSlots.isVideo(path) } : null; }
  const p = MediaGate.plan(it.m, ALLOW)[j];
  return p ? { url: p.url, alt: '', video: p.kind === 'video' } : null;
}
// A selection inside code is a code block: its fence (with the language) lies outside the cloned contents, so it is put back.
function codeWrapped(range, frag) {
  const at = range.commonAncestorContainer, el = at.nodeType === 1 ? at : at.parentElement, pre = el && el.closest('pre');
  if (!pre) return frag;
  const outer = pre.cloneNode(false), code = pre.querySelector('code');
  if (code) { const inner = code.cloneNode(false); inner.appendChild(frag); outer.appendChild(inner); } else outer.appendChild(frag);
  return outer;
}
// The parts of the selection, entry by entry: { i, whole, md }; the live row (plain streamed text) has no entry.
function selectionParts(sel) {
  const parts = [];
  for (let r = 0; r < sel.rangeCount; r++) {
    const range = sel.getRangeAt(r);
    for (const row of Array.from(items.children)) {
      if (!range.intersectsNode(row)) continue;
      const it = S.items[parseInt(row.getAttribute('data-k'), 10)], c = row.querySelector('.c'), b = row.querySelector('.b');
      if (!it || !c) continue;
      const sub = clampRange(range, c);
      if (sub.collapsed) continue;
      const md = MdCopy.fromNode(codeWrapped(sub, sub.cloneContents()), { media: el => mediaOf(el, it) });
      parts.push({ i: it.i, whole: !!b && !it.trunc && coversText(range, b), md: md });
    }
    if (range.intersectsNode(liveBox)) { const t = clampRange(range, liveBox).toString(); if (t.trim()) parts.push({ whole: false, md: t }); }
  }
  return parts;
}
// Posts the selection for the clipboard: kind 'text' (what Ctrl+C copies) or 'markdown'. False when nothing is selected in the transcript.
function copySelection(kind) {
  const sel = transcriptSelection();
  if (!sel) return false;
  if (kind === 'markdown') post({ ev: 'copy', tok: S.tok, kind: 'markdown', parts: selectionParts(sel) });
  else post({ ev: 'copy', kind: 'text', text: sel.toString() });
  return true;
}
