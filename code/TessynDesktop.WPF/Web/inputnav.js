// Input selection and keyboard navigation for the transcript page. Pure functions, so they run under node in the test suite (InputNavScriptTests).
// An "input" is a USER entry. Mine: typed by the user (no source, "web", "telegram"). Automated: "scheduled" (incl. mailbox deliveries) or
// "orchestrated" (a supervisor's prompt). From a selected automated input the arrow keys visit every input; from one of mine, only mine.
'use strict';
(function (root) {
  const AUTO_LABEL = { scheduled: 'SCHEDULED', orchestrated: 'SUPERVISOR' };
  const LINE_PX = 40;            // one arrow-key step with nothing selected
  const PAGE_FRACTION = 0.875;   // PgUp/PgDn move this much of the viewport

  function isInput(it) { return !!it && it.r === 'U'; }
  function isAuto(it) { return isInput(it) && Object.prototype.hasOwnProperty.call(AUTO_LABEL, it.s); }
  // The header label that replaces "USER" on an automated input; null otherwise.
  function labelOf(it) { return isAuto(it) ? AUTO_LABEL[it.s] : null; }

  function lowerBound(items, i) { let lo = 0, hi = items.length; while (lo < hi) { const mid = (lo + hi) >> 1; if (items[mid].i < i) lo = mid + 1; else hi = mid; } return lo; }
  function upperBound(items, i) { let lo = 0, hi = items.length; while (lo < hi) { const mid = (lo + hi) >> 1; if (items[mid].i <= i) lo = mid + 1; else hi = mid; } return lo; }

  // Position in `items` (ascending by entry index) of the nearest input before (dir < 0) or after (dir > 0) entry index `from`; -1 when the
  // loaded entries hold none. mineOnly skips automated inputs.
  function find(items, from, dir, mineOnly) {
    let k = dir > 0 ? upperBound(items, from) : lowerBound(items, from) - 1;
    for (; k >= 0 && k < items.length; k += dir) {
      const it = items[k];
      if (isInput(it) && (!mineOnly || !isAuto(it))) return k;
    }
    return -1;
  }

  // What a key does. `selected` is whether an input is selected. Keys with Ctrl/Alt/Meta/Shift are left alone (Ctrl+F, text selection, ...).
  // Returns { type: 'nav', dir } | { type: 'scroll', dy } | { type: 'clear' } | null.
  function keyAction(key, mods, selected, clientHeight) {
    if (mods && (mods.ctrl || mods.alt || mods.meta || mods.shift)) return null;
    const page = Math.max(LINE_PX, Math.floor(clientHeight * PAGE_FRACTION));
    switch (key) {
      case 'ArrowUp': return selected ? { type: 'nav', dir: -1 } : { type: 'scroll', dy: -LINE_PX };
      case 'ArrowDown': return selected ? { type: 'nav', dir: 1 } : { type: 'scroll', dy: LINE_PX };
      case 'PageUp': return { type: 'scroll', dy: -page };
      case 'PageDown': return { type: 'scroll', dy: page };
      case 'Escape': return selected ? { type: 'clear' } : null;
      default: return null;
    }
  }

  // scrollTop that puts the entry at position k near the top of the view, `margin` pixels below it (never negative).
  function revealTop(off, k, margin) { return Math.max(0, off[k] - margin); }

  const api = { isInput, isAuto, labelOf, find, keyAction, revealTop, LINE_PX, PAGE_FRACTION };
  root.InputNav = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
