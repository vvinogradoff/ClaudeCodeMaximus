// Scrollbar mapping of the transcript page. The list is virtualised: entries that were never rendered have ESTIMATED heights, so a native scrollbar
// (thumb position proportional to pixels) moves the content by an amount that changes whenever entries get measured, and a drag does not follow the
// mouse. This thumb is proportional to the ENTRY INDEX instead: its position is (entry at the top of the view + the fraction of it scrolled past) / (the
// same figure at the bottom of the scroll range). Entry heights only matter inside one entry, so measuring other entries cannot move the thumb or the
// content under a drag. Pure functions on the offsets array `off` (off[k] = top of entry k, off[n] = total height), so they run under node
// (ScrollMapScriptTests).
'use strict';
(function (root) {
  const MIN_THUMB_PX = 28;

  // Position in entry space of a scroll offset: k + fraction of entry k that is above the top of the view.
  function entryPos(off, scrollTop) {
    const n = off.length - 1; if (n <= 0) return 0;
    let lo = 0, hi = n - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (off[mid] <= scrollTop) lo = mid; else hi = mid - 1; }
    const h = off[lo + 1] - off[lo];
    return lo + (h > 0 ? Math.min(1, Math.max(0, (scrollTop - off[lo]) / h)) : 0);
  }

  // The scroll offset of an entry-space position (inverse of entryPos).
  function scrollOf(off, t) {
    const n = off.length - 1; if (n <= 0) return 0;
    if (t <= 0) return 0;
    if (t >= n) return off[n];
    const k = Math.floor(t);
    return off[k] + (t - k) * (off[k + 1] - off[k]);
  }

  // 0..1 position of the thumb along its travel for a scroll offset; 1 exactly at the bottom of the scroll range.
  function fractionOf(off, scrollTop, maxScroll) {
    if (!(maxScroll > 0)) return 0;
    const tMax = entryPos(off, maxScroll);
    if (!(tMax > 0)) return scrollTop >= maxScroll ? 1 : 0;
    return Math.min(1, Math.max(0, entryPos(off, scrollTop) / tMax));
  }

  // The scroll offset for a thumb at fraction f of its travel (inverse of fractionOf; f = 1 gives exactly maxScroll).
  function scrollForFraction(off, f, maxScroll) {
    if (!(maxScroll > 0)) return 0;
    const g = Math.min(1, Math.max(0, f));
    if (g >= 1) return maxScroll;
    return Math.min(maxScroll, Math.max(0, scrollOf(off, g * entryPos(off, maxScroll))));
  }

  // Thumb length in px: the visible share of the content, never shorter than MIN_THUMB_PX and never longer than the track.
  function thumbLength(trackPx, viewPx, totalPx) {
    if (!(trackPx > 0)) return 0;
    if (!(totalPx > 0) || totalPx <= viewPx) return trackPx;
    return Math.min(trackPx, Math.max(MIN_THUMB_PX, trackPx * viewPx / totalPx));
  }

  const api = { entryPos, scrollOf, fractionOf, scrollForFraction, thumbLength, MIN_THUMB_PX };
  root.ScrollMap = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
